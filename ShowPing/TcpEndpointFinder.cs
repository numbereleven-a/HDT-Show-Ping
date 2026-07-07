using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Management;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace ShowPing
{
    internal sealed class TcpEndpointFinder
    {
        private static readonly HashSet<ushort> KnownGamePorts = new HashSet<ushort> { 1119, 3724 };
        private const ushort PreferredGamePort = 1119;

        public bool IsAvailable { get; private set; } = true;
        public string LastError { get; private set; }

        public bool TryFindCurrentEndpoint(HashSet<uint> pids, out string address, out ushort port)
        {
            return TryFindCurrentEndpoint(pids, null, 0, out address, out port);
        }

        public bool TryFindCurrentEndpoint(HashSet<uint> pids, string preferredAddress, ushort preferredPort, out string address, out ushort port)
        {
            address = null;
            port = 0;

            if (pids == null || pids.Count == 0)
                return false;

            var connections = GetTcpConnections()
                .Where(x => pids.Contains(x.OwningProcess))
                .Where(x => IsUsefulRemoteAddress(x.Address))
                .ToList();

            var selected = connections.FirstOrDefault(x => MatchesPreferredEndpoint(x, preferredAddress, preferredPort));
            if (selected == null)
            {
                if (!string.IsNullOrWhiteSpace(preferredAddress) && preferredPort != 0)
                    return false;

                var candidates = connections
                    .Where(x => KnownGamePorts.Contains(x.Port))
                    .OrderBy(x => x.Port == PreferredGamePort ? 0 : 1)
                    .ThenByDescending(x => x.Port)
                    .ToList();

                if (candidates.Count == 0)
                    return false;
                if (candidates.Count > 1)
                {
                    Log.Info("ShowPing endpoint ambiguous: " + FormatCandidates(candidates));
                    return false;
                }

                selected = candidates[0];
            }

            address = selected.Address.ToString();
            port = selected.Port;
            return true;
        }

        private static bool MatchesPreferredEndpoint(EndpointCandidate candidate, string preferredAddress, ushort preferredPort)
        {
            if (string.IsNullOrWhiteSpace(preferredAddress) || preferredPort == 0 || candidate.Port != preferredPort)
                return false;

            IPAddress preferred;
            if (!IPAddress.TryParse(preferredAddress.Trim('[', ']'), out preferred))
                return false;

            if (candidate.Address.IsIPv4MappedToIPv6)
                return candidate.Address.MapToIPv4().Equals(preferred);
            if (preferred.IsIPv4MappedToIPv6)
                preferred = preferred.MapToIPv4();

            return candidate.Address.Equals(preferred);
        }

        private List<EndpointCandidate> GetTcpConnections()
        {
            var candidates = new List<EndpointCandidate>();
            try
            {
                var options = new EnumerationOptions
                {
                    Timeout = TimeSpan.FromSeconds(1),
                    ReturnImmediately = true
                };

                using (var searcher = new ManagementObjectSearcher(
                           new ManagementScope(@"root\StandardCimv2"),
                           new ObjectQuery("SELECT RemoteAddress, RemotePort, OwningProcess FROM MSFT_NetTCPConnection WHERE State = 5"),
                           options))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject row in results)
                    {
                        using (row)
                        {
                            try
                            {
                                var remoteAddress = Convert.ToString(row["RemoteAddress"]);
                                if (string.IsNullOrWhiteSpace(remoteAddress))
                                    continue;

                                IPAddress address;
                                ushort port;
                                if (!IPAddress.TryParse(remoteAddress, out address))
                                    continue;
                                if (!ushort.TryParse(Convert.ToString(row["RemotePort"]), out port))
                                    continue;

                                var owningProcessValue = row["OwningProcess"];
                                if (owningProcessValue == null)
                                    continue;

                                var owningProcess = Convert.ToUInt32(owningProcessValue);
                                candidates.Add(new EndpointCandidate(address, port, owningProcess));
                            }
                            catch (Exception ex)
                            {
                                Log.Info("ShowPing TCP endpoint row skipped: " + ex.Message);
                            }
                        }
                    }
                }

                IsAvailable = true;
                LastError = null;
            }
            catch (ManagementException ex)
            {
                IsAvailable = false;
                LastError = ex.Message;
                Log.Info("ShowPing TCP endpoint query unavailable: " + ex.Message);
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                LastError = ex.Message;
                Log.Info("ShowPing TCP endpoint query failed: " + ex.Message);
            }

            return candidates;
        }

        private static bool IsUsefulRemoteAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (IPAddress.IsLoopback(address))
                return false;
            if (IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address))
                return false;

            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                if ((bytes[0] & 0xF0) == 0xE0)
                    return false;
                if (bytes[0] == 10)
                    return false;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    return false;
                if (bytes[0] == 192 && bytes[1] == 168)
                    return false;
                if (bytes[0] == 169 && bytes[1] == 254)
                    return false;
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                    return false;
                return true;
            }

            if (address.AddressFamily != AddressFamily.InterNetworkV6)
                return false;

            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                return false;
            if (bytes.Length > 0 && (bytes[0] & 0xFE) == 0xFC)
                return false;

            return true;
        }

        private sealed class EndpointCandidate
        {
            public EndpointCandidate(IPAddress address, ushort port, uint owningProcess)
            {
                Address = address;
                Port = port;
                OwningProcess = owningProcess;
            }

            public IPAddress Address { get; }
            public ushort Port { get; }
            public uint OwningProcess { get; }
        }

        private static string FormatCandidates(IEnumerable<EndpointCandidate> candidates)
        {
            return string.Join(", ", candidates.Select(x => x.Address + ":" + x.Port));
        }
    }
}
