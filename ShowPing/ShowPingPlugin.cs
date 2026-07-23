using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;

namespace ShowPing
{
    public sealed class ShowPingPlugin : IPlugin
    {
        private ShowPingSettings settings;
        private NetworkMonitor monitor;
        private NetworkOverlayControl overlay;
        private OverlayPlacementController placement;
        private Canvas overlayCanvas;
        private Canvas sizeChangedCanvas;
        private SettingsWindow settingsWindow;
        private bool suppressSettingsWindowClose;
        private bool previewActive;
        private ShowPingSettings previewSettings;
        private DispatcherTimer previewTimer;
        private int previewFailureIndex;
        private bool overlayPositionInitialized;
        private volatile NetworkSnapshot snapshot = NetworkSnapshot.Empty;
        private DateTime nextPositionUpdate = DateTime.MinValue;
        private SizeChangedEventHandler sizeChangedHandler;

        public string Name => "ShowPing";
        public string Description =>
            "Shows Hearthstone server TCP latency and failed checks on a separate network overlay.\n" +
            "https://github.com/numbereleven-a/HDT-Show-Ping";
        public string ButtonText => "Settings";
        public string Author => "numbereleven-a";
        public Version Version => new Version(1, 5);
        public MenuItem MenuItem { get; private set; }

        public void OnLoad()
        {
            CleanupRuntime();
            try
            {
                settings = SettingsStore.Load();
                CreateMenuItem();
                EnsureOverlay(false);

                monitor = new NetworkMonitor(settings);
                monitor.SnapshotChanged += Monitor_SnapshotChanged;
                monitor.Start();
            }
            catch
            {
                CleanupRuntime();
                MenuItem = null;
                throw;
            }
        }

        public void OnUnload()
        {
            CleanupRuntime();

            if (settings != null)
                SettingsStore.Save(settings);
            MenuItem = null;
        }

        private void CleanupRuntime()
        {
            StopPreview(false);

            if (settingsWindow != null)
            {
                suppressSettingsWindowClose = true;
                settingsWindow.Close();
                settingsWindow = null;
                suppressSettingsWindowClose = false;
            }

            if (sizeChangedHandler != null && sizeChangedCanvas != null)
            {
                sizeChangedCanvas.SizeChanged -= sizeChangedHandler;
                sizeChangedCanvas = null;
                sizeChangedHandler = null;
            }

            RemoveOverlay();

            if (monitor != null)
            {
                monitor.SnapshotChanged -= Monitor_SnapshotChanged;
                monitor.Dispose();
                monitor = null;
            }
        }

        public void OnButtonPress()
        {
            if (settingsWindow != null)
            {
                if (settingsWindow.WindowState == WindowState.Minimized)
                    settingsWindow.WindowState = WindowState.Normal;
                settingsWindow.Activate();
                return;
            }

            var committedSettings = settings.Clone();
            var window = new SettingsWindow(
                settings,
                Version,
                nextSettings =>
                {
                    settings = nextSettings;
                    committedSettings = nextSettings.Clone();
                    ApplySettings(false);
                },
                StartPreview,
                () => StopPreview(true));
            settingsWindow = window;
            window.Closed += (sender, args) =>
            {
                if (ReferenceEquals(settingsWindow, window))
                    settingsWindow = null;
                if (suppressSettingsWindowClose)
                    return;

                var positionedSettings = settings;
                StopPreview(false);
                var selectedSettings = window.Accepted ? window.ResultSettings : committedSettings;
                CopyOverlayPosition(positionedSettings, selectedSettings);
                settings = selectedSettings;
                ApplySettings(false);
            };
            window.Show();
        }

        public void OnUpdate()
        {
            if (overlay == null)
            {
                if (previewActive)
                {
                    EnsureOverlay(true);
                    RenderPreview();
                }
                else if (settings?.ShowServerPing == true)
                {
                    EnsureOverlay(false);
                }
                return;
            }

            if (DateTime.UtcNow >= nextPositionUpdate)
            {
                if (TryGetOverlayCanvas() != overlayCanvas)
                {
                    EnsureOverlay(previewActive);
                    if (previewActive)
                        RenderPreview();
                }
                PositionOverlay(false);
                nextPositionUpdate = DateTime.UtcNow.AddSeconds(1);
            }
        }

        private void CreateMenuItem()
        {
            MenuItem = new MenuItem
            {
                Header = "ShowPing"
            };
            MenuItem.Click += (sender, args) => OnButtonPress();
        }

        private void ApplySettings(bool resetPosition)
        {
            SettingsStore.Save(settings);
            monitor.ApplySettings(settings);
            if (previewActive)
            {
                EnsureOverlay(true);
                placement?.UpdateSettings(settings);
                RenderPreview();
                return;
            }

            if (!settings.ShowServerPing)
            {
                RemoveOverlay();
                return;
            }

            EnsureOverlay(false);
            if (overlay != null)
            {
                placement?.UpdateSettings(settings);
                overlay.ApplySettings(settings);
                overlay.SetNetworkState(snapshot, settings);
                overlay.Visibility = Visibility.Visible;
                PositionOverlay(resetPosition);
            }
        }

        private void EnsureOverlay(bool force)
        {
            if (!force && !settings.ShowServerPing)
            {
                RemoveOverlay();
                return;
            }

            var canvas = TryGetOverlayCanvas();
            if (canvas == null)
                return;

            EnsureSizeChangedHandler(canvas);

            if (overlay != null && overlayCanvas == canvas)
                return;
            if (overlay != null)
                RemoveOverlay();

            overlay = new NetworkOverlayControl();
            overlay.ApplySettings(settings);
            overlay.SetNetworkState(snapshot, settings);
            overlay.Visibility = Visibility.Visible;
            canvas.Children.Add(overlay);
            Panel.SetZIndex(overlay, 1000);
            overlayCanvas = canvas;
            placement = new OverlayPlacementController(overlay, settings, () => SettingsStore.Save(settings), canvas);
            PositionOverlay(false);
        }

        private void StartPreview(ShowPingSettings nextSettings)
        {
            var wasActive = previewActive;
            previewSettings = nextSettings.Clone();
            previewSettings.Normalize();
            previewActive = true;
            if (!wasActive)
                previewFailureIndex = 0;

            EnsureOverlay(true);
            placement?.UpdateSettings(settings);
            if (placement != null)
                placement.ForceMovable = true;
            RenderPreview();

            if (previewTimer == null)
            {
                previewTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                previewTimer.Tick += PreviewTimer_Tick;
            }
            previewTimer.Start();
        }

        private void StopPreview(bool restoreOverlay)
        {
            previewActive = false;
            previewSettings = null;
            if (previewTimer != null)
                previewTimer.Stop();
            if (placement != null)
                placement.ForceMovable = false;

            if (!restoreOverlay || settings == null)
                return;

            if (!settings.ShowServerPing)
            {
                RemoveOverlay();
                return;
            }

            EnsureOverlay(false);
            if (overlay == null)
                return;

            placement?.UpdateSettings(settings);
            overlay.ApplySettings(settings);
            overlay.SetNetworkState(snapshot, settings);
            overlay.Visibility = Visibility.Visible;
            PositionOverlay(false);
        }

        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            previewFailureIndex = (previewFailureIndex + 1) % 3;
            RenderPreview();
        }

        private void RenderPreview()
        {
            if (!previewActive || previewSettings == null || overlay == null)
                return;

            var failurePercent = previewFailureIndex == 0 ? 20 : previewFailureIndex == 1 ? 40 : 0;
            var previewSnapshot = NetworkSnapshot.Success(
                42,
                failurePercent,
                "12.34.56.78",
                1119,
                "12.34.56.78");

            overlay.ApplySettings(previewSettings);
            overlay.SetNetworkState(previewSnapshot, previewSettings, "EU");
            overlay.Visibility = Visibility.Visible;
            if (placement != null)
                placement.ForceMovable = true;
            PositionOverlay(false);
        }

        private static void CopyOverlayPosition(ShowPingSettings source, ShowPingSettings destination)
        {
            if (source == null || destination == null || !source.NetworkOverlayManualPosition)
                return;

            destination.NetworkOverlayManualPosition = true;
            destination.NetworkOverlayLeft = source.NetworkOverlayLeft;
            destination.NetworkOverlayTop = source.NetworkOverlayTop;
            destination.NetworkOverlayWidth = source.NetworkOverlayWidth;
            destination.NetworkOverlayHeight = source.NetworkOverlayHeight;
        }

        private void RemoveOverlay()
        {
            if (overlay == null)
                return;

            placement?.Dispose();
            placement = null;
            var canvas = overlayCanvas ?? TryGetOverlayCanvas();
            canvas?.Children.Remove(overlay);
            overlayCanvas = null;
            overlay = null;
            overlayPositionInitialized = false;
        }

        private void Monitor_SnapshotChanged(NetworkSnapshot nextSnapshot)
        {
            snapshot = nextSnapshot;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            Action update = () =>
            {
                if (!previewActive && overlay != null && settings != null)
                    overlay.SetNetworkState(snapshot, settings);
            };

            try
            {
                if (dispatcher.CheckAccess())
                    update();
                else
                    dispatcher.BeginInvoke(update);
            }
            catch
            {
            }
        }

        private void PositionOverlay(bool force)
        {
            var canvas = overlayCanvas ?? TryGetOverlayCanvas();
            if (overlay == null || canvas == null || placement?.IsMoving == true)
                return;

            var canvasWidth = canvas.ActualWidth;
            var canvasHeight = canvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
                return;
            if (!force && overlayPositionInitialized)
            {
                placement?.UpdateGrip();
                return;
            }

            if (settings.NetworkOverlayManualPosition && !force)
            {
                var savedLeft = settings.NetworkOverlayLeft;
                var savedTop = settings.NetworkOverlayTop;
                ClampToCanvas(ref savedLeft, ref savedTop);
                SetOverlayPosition(savedLeft, savedTop);
                overlayPositionInitialized = true;
                return;
            }

            double left;
            double top;
            GetDefaultPosition(out left, out top);
            SetOverlayPosition(left, top);
            overlayPositionInitialized = true;
        }

        private void GetDefaultPosition(out double left, out double top)
        {
            var canvas = overlayCanvas ?? TryGetOverlayCanvas();
            var width = canvas?.ActualWidth ?? 0;
            var height = canvas?.ActualHeight ?? 0;

            var overlayWidth = GetOverlayPlacementWidth();
            var overlayHeight = GetOverlayPlacementHeight();

            left = width - overlayWidth - 24;
            top = height - overlayHeight - 170;

            if (left < 0)
                left = 0;
            if (top < 0)
                top = 0;
        }

        private void SetOverlayPosition(double left, double top)
        {
            Canvas.SetLeft(overlay, left);
            Canvas.SetTop(overlay, top);
            placement?.UpdateGrip();
        }

        private void ClampToCanvas(ref double left, ref double top)
        {
            var canvas = overlayCanvas ?? TryGetOverlayCanvas();
            if (overlay == null || canvas == null)
                return;

            var canvasWidth = canvas.ActualWidth;
            var canvasHeight = canvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
                return;

            var maxLeft = Math.Max(0, canvasWidth - GetOverlayWidth());
            var maxTop = Math.Max(0, canvasHeight - GetOverlayHeight());
            left = Math.Max(0, Math.Min(maxLeft, left));
            top = Math.Max(0, Math.Min(maxTop, top));
        }

        private double GetOverlayWidth()
        {
            if (overlay == null)
                return settings.NetworkOverlayWidth;
            if (overlay.ActualWidth > 0)
                return overlay.ActualWidth;
            if (!double.IsNaN(overlay.Width) && overlay.Width > 0)
                return overlay.Width;
            return overlay.MinWidth > 0 ? overlay.MinWidth : settings.NetworkOverlayWidth;
        }

        private double GetOverlayHeight()
        {
            if (overlay == null)
                return settings.NetworkOverlayHeight;
            if (overlay.ActualHeight > 0)
                return overlay.ActualHeight;
            if (!double.IsNaN(overlay.Height) && overlay.Height > 0)
                return overlay.Height;
            return overlay.MinHeight > 0 ? overlay.MinHeight : settings.NetworkOverlayHeight;
        }

        private double GetOverlayPlacementWidth()
        {
            return overlay == null
                ? settings.NetworkOverlayWidth
                : Math.Max(GetOverlayWidth(), overlay.ReservedPlacementWidth);
        }

        private double GetOverlayPlacementHeight()
        {
            return overlay == null
                ? settings.NetworkOverlayHeight
                : Math.Max(GetOverlayHeight(), overlay.ReservedPlacementHeight);
        }

        private void EnsureSizeChangedHandler(Canvas canvas)
        {
            if (canvas == null)
                return;

            if (sizeChangedHandler == null)
            {
                sizeChangedHandler = (sender, args) =>
                {
                    overlayPositionInitialized = false;
                    PositionOverlay(false);
                };
            }

            if (sizeChangedCanvas == canvas)
                return;

            if (sizeChangedCanvas != null)
                sizeChangedCanvas.SizeChanged -= sizeChangedHandler;

            sizeChangedCanvas = canvas;
            sizeChangedCanvas.SizeChanged += sizeChangedHandler;
        }

        private static Canvas TryGetOverlayCanvas()
        {
            try
            {
                return Core.OverlayCanvas;
            }
            catch
            {
                return null;
            }
        }
    }
}
