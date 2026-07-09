using System;
using System.Windows;
using System.Windows.Controls;
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
        private volatile NetworkSnapshot snapshot = NetworkSnapshot.Empty;
        private DateTime nextPositionUpdate = DateTime.MinValue;
        private SizeChangedEventHandler sizeChangedHandler;

        public string Name => "ShowPing";
        public string Description => "Shows Hearthstone server TCP latency and failed checks on a separate network overlay.";
        public string ButtonText => "Settings";
        public string Author => "numbereleven-a";
        public Version Version => new Version(1, 1, 0);
        public MenuItem MenuItem { get; private set; }

        public void OnLoad()
        {
            settings = SettingsStore.Load();
            CreateMenuItem();
            monitor = new NetworkMonitor(settings);
            monitor.SnapshotChanged += Monitor_SnapshotChanged;
            monitor.Start();
            EnsureOverlay();
        }

        public void OnUnload()
        {
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

            SettingsStore.Save(settings);
        }

        public void OnButtonPress()
        {
            var backup = settings.Clone();
            var window = new SettingsWindow(settings, Version, nextSettings =>
            {
                settings = nextSettings;
                ApplySettings(false);
            });
            if (window.ShowDialog() == true)
            {
                settings = window.ResultSettings;
                ApplySettings(false);
            }
            else
            {
                settings = backup;
                ApplySettings(false);
            }
        }

        public void OnUpdate()
        {
            if (overlay == null)
            {
                if (settings?.ShowServerPing == true)
                    EnsureOverlay();
                return;
            }

            if (DateTime.UtcNow >= nextPositionUpdate)
            {
                if (TryGetOverlayCanvas() != overlayCanvas)
                    EnsureOverlay();
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
            if (!settings.ShowServerPing)
            {
                RemoveOverlay();
                return;
            }

            EnsureOverlay();
            if (overlay != null)
            {
                placement?.UpdateSettings(settings);
                overlay.ApplySettings(settings);
                overlay.SetNetworkState(snapshot, settings);
                overlay.Visibility = Visibility.Visible;
                PositionOverlay(resetPosition);
            }
        }

        private void EnsureOverlay()
        {
            if (!settings.ShowServerPing)
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
        }

        private void Monitor_SnapshotChanged(NetworkSnapshot nextSnapshot)
        {
            snapshot = nextSnapshot;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            Action update = () =>
            {
                if (overlay != null && settings != null)
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

            if (settings.NetworkOverlayManualPosition && !force)
            {
                var savedLeft = settings.NetworkOverlayLeft;
                var savedTop = settings.NetworkOverlayTop;
                ClampToCanvas(ref savedLeft, ref savedTop);
                SetOverlayPosition(savedLeft, savedTop);
                return;
            }

            double left;
            double top;
            GetDefaultPosition(out left, out top);
            SetOverlayPosition(left, top);
        }

        private void GetDefaultPosition(out double left, out double top)
        {
            var canvas = overlayCanvas ?? TryGetOverlayCanvas();
            var width = canvas?.ActualWidth ?? 0;
            var height = canvas?.ActualHeight ?? 0;

            var overlayWidth = GetOverlayWidth();
            var overlayHeight = GetOverlayHeight();

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

        private void EnsureSizeChangedHandler(Canvas canvas)
        {
            if (canvas == null)
                return;

            if (sizeChangedHandler == null)
                sizeChangedHandler = (sender, args) => PositionOverlay(false);

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
