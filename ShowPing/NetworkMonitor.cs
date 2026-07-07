using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Enums.Hearthstone;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace ShowPing
{
    internal sealed class NetworkMonitor : IDisposable
    {
        private const int TimeoutMilliseconds = 1500;
        private const int PacketLossSampleSize = 10;

        private readonly TcpEndpointFinder tcpEndpointFinder = new TcpEndpointFinder();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly Queue<bool> pingHistory = new Queue<bool>();
        private readonly object sync = new object();
        private CancellationTokenSource refreshCancellation = new CancellationTokenSource();
        private int pingInProgress;
        private volatile bool disposed;
        private int settingsVersion;
        private string lastEndpointKey;
        private ShowPingSettings settings;

        public NetworkMonitor(ShowPingSettings settings)
        {
            this.settings = settings.Clone();
            timer.Interval = TimeSpan.FromSeconds(this.settings.CheckIntervalSeconds);
            timer.Tick += Timer_Tick;
        }

        public event Action<NetworkSnapshot> SnapshotChanged;

        public void Start()
        {
            ApplySettings(settings);
        }

        public void ApplySettings(ShowPingSettings nextSettings)
        {
            bool enabled;
            lock (sync)
            {
                settings = nextSettings.Clone();
                settingsVersion++;
                timer.Interval = GetTimerInterval(settings);
                enabled = settings.ShowServerPing;
            }

            if (enabled)
            {
                if (!timer.IsEnabled)
                    timer.Start();
                _ = RefreshAsync();
            }
            else
            {
                timer.Stop();
                lock (sync)
                {
                    CancelCurrentRefreshLocked();
                    pingHistory.Clear();
                    lastEndpointKey = null;
                }
                Publish(NetworkSnapshot.Empty);
            }
        }

        public void Dispose()
        {
            disposed = true;
            timer.Stop();
            timer.Tick -= Timer_Tick;
            lock (sync)
            {
                CancelCurrentRefreshLocked(false);
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            int version;
            bool enabled;
            CancellationToken token;
            lock (sync)
            {
                version = settingsVersion;
                enabled = settings.ShowServerPing;
                token = refreshCancellation.Token;
            }

            if (!enabled || disposed || Interlocked.CompareExchange(ref pingInProgress, 1, 0) != 0)
                return;

            try
            {
                var hasActiveGame = HasActiveGame();
                var snapshot = await BuildSnapshotAsync(hasActiveGame, token).ConfigureAwait(false);

                lock (sync)
                {
                    if (disposed || version != settingsVersion || !settings.ShowServerPing)
                        return;
                }

                Publish(snapshot);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error("ShowPing check error:\n" + ex);
                lock (sync)
                {
                    if (disposed || version != settingsVersion || !settings.ShowServerPing)
                        return;
                }
                Publish(NetworkSnapshot.Unavailable("error", GetFailurePercent(), null, 0));
            }
            finally
            {
                Interlocked.Exchange(ref pingInProgress, 0);
            }
        }

        private async Task<NetworkSnapshot> BuildSnapshotAsync(bool hasActiveGame, CancellationToken token)
        {
            if (!hasActiveGame)
            {
                lock (sync)
                {
                    pingHistory.Clear();
                    lastEndpointKey = null;
                }
                return NetworkSnapshot.Empty;
            }

            token.ThrowIfCancellationRequested();
            var endpoint = await Task.Run(() => GetCurrentEndpoint(), token).ConfigureAwait(false);
            if (endpoint == null)
            {
                lock (sync)
                {
                    pingHistory.Clear();
                    lastEndpointKey = null;
                }
                return tcpEndpointFinder.IsAvailable
                    ? NetworkSnapshot.NoTarget("no target")
                    : NetworkSnapshot.NoTarget("tcp table unavailable");
            }

            ResetHistoryIfEndpointChanged(endpoint.Address, endpoint.Port);

            var probe = await MeasureTcpConnectAsync(endpoint.Address, endpoint.Port, token).ConfigureAwait(false);
            if (probe.Success)
            {
                RecordPingResult(true);
                return NetworkSnapshot.Success(probe.Milliseconds, GetFailurePercent(), endpoint.Address, endpoint.Port);
            }

            if (probe.CountAsNetworkFailure)
                RecordPingResult(false);

            return NetworkSnapshot.Unavailable(probe.Status, GetFailurePercent(), endpoint.Address, endpoint.Port);
        }

        private Endpoint GetCurrentEndpoint()
        {
            var endpoint = TryGetHdtServerInfoEndpoint();
            if (endpoint != null)
                return endpoint;

            var pids = GetHearthstonePids();
            string address;
            ushort port;
            if (tcpEndpointFinder.TryFindCurrentEndpoint(pids, out address, out port))
                return new Endpoint(address, port);

            return null;
        }

        private static Endpoint TryGetHdtServerInfoEndpoint()
        {
            try
            {
                var game = Core.Game;
                if (game == null)
                    return null;

                var metaData = GetMemberValue(game, "MetaData");
                var serverInfo = metaData != null ? GetMemberValue(metaData, "ServerInfo") : null;
                if (serverInfo == null)
                    return null;

                var address = Convert.ToString(GetMemberValue(serverInfo, "Address"));
                var portValue = GetMemberValue(serverInfo, "Port");
                if (string.IsNullOrWhiteSpace(address) || portValue == null)
                    return null;

                var port = Convert.ToInt32(portValue);
                if (port <= 0 || port > 65535)
                    return null;

                IPAddress parsed;
                if (!IPAddress.TryParse(address.Trim('[', ']'), out parsed))
                    return null;

                return new Endpoint(address, (ushort)port);
            }
            catch (Exception ex)
            {
                Log.Info("ShowPing HDT server info unavailable: " + ex.Message);
                return null;
            }
        }

        private static object GetMemberValue(object source, string name)
        {
            var type = source.GetType();
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null)
                return property.GetValue(source, null);

            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            return field?.GetValue(source);
        }

        private static bool HasActiveGame()
        {
            var game = Core.Game;
            if (game == null)
                return false;

            if (game.IsInMenu || !game.IsRunning)
                return false;

            if (game.CurrentGameMode == GameMode.None)
                return false;

            var stats = game.CurrentGameStats;
            return stats == null || stats.EndTime <= stats.StartTime;
        }

        private static HashSet<uint> GetHearthstonePids()
        {
            var processes = Process.GetProcessesByName("Hearthstone");
            try
            {
                return new HashSet<uint>(processes.Select(process => (uint)process.Id));
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        private static async Task<ProbeResult> MeasureTcpConnectAsync(string address, ushort port, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return ProbeResult.InternalError("cancelled");

            IPAddress ip;
            if (!IPAddress.TryParse(address.Trim('[', ']'), out ip))
                return ProbeResult.InternalError("bad endpoint");

            if (ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();

            var stopwatch = Stopwatch.StartNew();
            using (var client = new TcpClient(ip.AddressFamily))
            using (token.Register(client.Close))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(TimeoutMilliseconds, cts.Token);
                var completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                    return ProbeResult.InternalError("cancelled");

                if (completed != connectTask)
                {
                    client.Close();
                    _ = connectTask.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    return ProbeResult.NetworkFailure("timeout");
                }

                cts.Cancel();
                try
                {
                    await connectTask.ConfigureAwait(false);
                    stopwatch.Stop();
                    return ProbeResult.Ok(stopwatch.ElapsedMilliseconds);
                }
                catch (SocketException ex)
                {
                    return ProbeResult.NetworkFailure(SocketStatus.Format(ex.SocketErrorCode));
                }
                catch
                {
                    return ProbeResult.InternalError("error");
                }
            }
        }

        private static TimeSpan GetTimerInterval(ShowPingSettings settings)
        {
            return TimeSpan.FromSeconds(settings.CheckIntervalSeconds);
        }

        private void ResetHistoryIfEndpointChanged(string address, ushort port)
        {
            var endpointKey = address + ":" + port;
            lock (sync)
            {
                if (endpointKey == lastEndpointKey)
                    return;

                lastEndpointKey = endpointKey;
                pingHistory.Clear();
            }
        }

        private void CancelCurrentRefreshLocked()
        {
            CancelCurrentRefreshLocked(true);
        }

        private void CancelCurrentRefreshLocked(bool replace)
        {
            var previous = refreshCancellation;
            if (replace)
                refreshCancellation = new CancellationTokenSource();
            previous.Cancel();
            previous.Dispose();
        }

        private void RecordPingResult(bool success)
        {
            lock (sync)
            {
                pingHistory.Enqueue(success);
                while (pingHistory.Count > PacketLossSampleSize)
                    pingHistory.Dequeue();
            }
        }

        private int GetFailurePercent()
        {
            lock (sync)
            {
                if (pingHistory.Count == 0)
                    return 0;

                var failed = 0;
                foreach (var success in pingHistory)
                {
                    if (!success)
                        failed++;
                }

                return (int)Math.Round((failed * 100.0) / pingHistory.Count);
            }
        }

        private void Publish(NetworkSnapshot snapshot)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    internal sealed class NetworkSnapshot
    {
        public static readonly NetworkSnapshot Empty = new NetworkSnapshot("PING: --", "CHECK FAIL: --", Brushes.Gray, null, false);
        private NetworkSnapshot(string pingText, string lossText, Brush brush, string endpoint, bool hasMeasurement)
        {
            PingText = pingText;
            LossText = lossText;
            Brush = brush;
            Endpoint = endpoint;
            HasMeasurement = hasMeasurement;
        }

        public string PingText { get; }
        public string LossText { get; }
        public Brush Brush { get; }
        public string Endpoint { get; }
        public bool HasMeasurement { get; }

        public static NetworkSnapshot Success(long milliseconds, int failedPercent, string address, ushort port)
        {
            return new NetworkSnapshot(
                "PING: " + milliseconds + " ms",
                "CHECK FAIL: " + failedPercent + "%",
                GetPingBrush(milliseconds),
                FormatEndpoint(address, port),
                true);
        }

        public static NetworkSnapshot NoTarget(string reason)
        {
            return new NetworkSnapshot("PING: " + reason, "CHECK FAIL: --", Brushes.Gray, null, false);
        }

        public static NetworkSnapshot Unavailable(string status, int failedPercent, string address, ushort port)
        {
            return new NetworkSnapshot(
                "PING: " + status,
                "CHECK FAIL: " + failedPercent + "%",
                Brushes.Red,
                FormatEndpoint(address, port),
                true);
        }

        private static Brush GetPingBrush(long milliseconds)
        {
            if (milliseconds >= 300)
                return Brushes.Red;
            if (milliseconds >= 100)
                return Brushes.Goldenrod;
            return Brushes.ForestGreen;
        }

        private static string FormatEndpoint(string address, ushort port)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;
            var formattedAddress = address.Contains(":") ? "[" + address + "]" : address;
            return port > 0 ? formattedAddress + ":" + port : formattedAddress;
        }
    }

    internal sealed class ProbeResult
    {
        private ProbeResult(bool success, long milliseconds, string status, bool countAsNetworkFailure)
        {
            Success = success;
            Milliseconds = milliseconds;
            Status = status;
            CountAsNetworkFailure = countAsNetworkFailure;
        }

        public bool Success { get; }
        public long Milliseconds { get; }
        public string Status { get; }
        public bool CountAsNetworkFailure { get; }

        public static ProbeResult Ok(long milliseconds)
        {
            return new ProbeResult(true, milliseconds, null, false);
        }

        public static ProbeResult NetworkFailure(string status)
        {
            return new ProbeResult(false, 0, status, true);
        }

        public static ProbeResult InternalError(string status)
        {
            return new ProbeResult(false, 0, status, false);
        }
    }

    internal static class SocketStatus
    {
        public static string Format(SocketError error)
        {
            switch (error)
            {
                case SocketError.ConnectionRefused:
                    return "refused";
                case SocketError.ConnectionReset:
                    return "reset";
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                    return "unreachable";
                case SocketError.TimedOut:
                    return "timeout";
                default:
                    return "socket error";
            }
        }
    }

    internal sealed class Endpoint
    {
        public Endpoint(string address, ushort port)
        {
            Address = address;
            Port = port;
        }

        public string Address { get; }
        public ushort Port { get; }
    }
}
