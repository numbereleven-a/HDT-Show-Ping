using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Utility.Extensions;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace ShowPing
{
    internal sealed class OverlayPlacementController : IDisposable
    {
        private readonly NetworkOverlayControl overlay;
        private readonly Action saveSettings;
        private readonly Canvas canvas;
        private readonly Thumb moveThumb = new Thumb();
        private static FieldInfo moveModeField;
        private static bool moveModeFieldResolved;
        private static bool moveModeFieldWarningLogged;
        private ShowPingSettings settings;
        private bool isMoving;
        private bool lastMoveMode;

        public OverlayPlacementController(NetworkOverlayControl overlay, ShowPingSettings settings, Action saveSettings, Canvas canvas)
        {
            this.overlay = overlay;
            this.settings = settings;
            this.saveSettings = saveSettings;
            this.canvas = canvas;

            moveThumb.Cursor = Cursors.SizeAll;
            moveThumb.Opacity = 0.01;
            moveThumb.Background = Brushes.Transparent;
            moveThumb.Visibility = Visibility.Collapsed;
            moveThumb.DragStarted += MoveThumb_DragStarted;
            moveThumb.DragDelta += MoveThumb_DragDelta;
            moveThumb.DragCompleted += MoveThumb_DragCompleted;
            OverlayExtensions.SetIsOverlayHitTestVisible(moveThumb, true);

            if (canvas != null && !canvas.Children.Contains(moveThumb))
                canvas.Children.Add(moveThumb);
        }

        public bool IsMoving => isMoving;

        public void UpdateSettings(ShowPingSettings nextSettings)
        {
            settings = nextSettings;
        }

        public void Dispose()
        {
            moveThumb.DragStarted -= MoveThumb_DragStarted;
            moveThumb.DragDelta -= MoveThumb_DragDelta;
            moveThumb.DragCompleted -= MoveThumb_DragCompleted;
            if (canvas != null && canvas.Children.Contains(moveThumb))
                canvas.Children.Remove(moveThumb);
        }

        public void UpdateGrip()
        {
            var moveMode = IsHdtMoveModeActive();
            if (moveMode != lastMoveMode)
            {
                moveThumb.Visibility = moveMode ? Visibility.Visible : Visibility.Collapsed;
                lastMoveMode = moveMode;
            }

            if (!moveMode)
                return;

            moveThumb.Width = GetElementWidth(overlay);
            moveThumb.Height = GetElementHeight(overlay);
            Canvas.SetLeft(moveThumb, Canvas.GetLeft(overlay));
            Canvas.SetTop(moveThumb, Canvas.GetTop(overlay));
        }

        private void MoveThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            isMoving = true;
        }

        private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var left = Canvas.GetLeft(overlay);
            var top = Canvas.GetTop(overlay);
            if (double.IsNaN(left))
                left = 0;
            if (double.IsNaN(top))
                top = 0;

            SetPosition(left + e.HorizontalChange, top + e.VerticalChange);
        }

        private void MoveThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            isMoving = false;
            settings.NetworkOverlayLeft = Canvas.GetLeft(overlay);
            settings.NetworkOverlayTop = Canvas.GetTop(overlay);
            settings.NetworkOverlayManualPosition = true;
            saveSettings();
        }

        private void SetPosition(double left, double top)
        {
            if (canvas == null)
                return;

            var canvasWidth = canvas.ActualWidth;
            var canvasHeight = canvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
                return;

            var maxLeft = Math.Max(0, canvasWidth - GetElementWidth(overlay));
            var maxTop = Math.Max(0, canvasHeight - GetElementHeight(overlay));
            left = Math.Max(0, Math.Min(maxLeft, left));
            top = Math.Max(0, Math.Min(maxTop, top));

            Canvas.SetLeft(overlay, left);
            Canvas.SetTop(overlay, top);
            Canvas.SetLeft(moveThumb, left);
            Canvas.SetTop(moveThumb, top);
        }

        private static bool IsHdtMoveModeActive()
        {
            var window = Core.OverlayWindow;
            if (window == null)
                return false;

            try
            {
                if (!moveModeFieldResolved)
                {
                    moveModeField = window.GetType().GetField("_uiMovable", BindingFlags.Instance | BindingFlags.NonPublic);
                    moveModeFieldResolved = true;
                    if (moveModeField == null && !moveModeFieldWarningLogged)
                    {
                        Log.Info("ShowPing overlay movement unavailable: HDT move-mode field not found.");
                        moveModeFieldWarningLogged = true;
                    }
                }

                if (moveModeField == null)
                    return false;

                return moveModeField.GetValue(window) is bool value && value;
            }
            catch (Exception ex)
            {
                if (!moveModeFieldWarningLogged)
                {
                    Log.Info("ShowPing overlay movement unavailable: " + ex.Message);
                    moveModeFieldWarningLogged = true;
                }
                return false;
            }
        }

        private static double GetElementWidth(FrameworkElement element)
        {
            if (element.ActualWidth > 0)
                return element.ActualWidth;
            if (!double.IsNaN(element.Width) && element.Width > 0)
                return element.Width;
            return Math.Max(0, element.MinWidth);
        }

        private static double GetElementHeight(FrameworkElement element)
        {
            if (element.ActualHeight > 0)
                return element.ActualHeight;
            if (!double.IsNaN(element.Height) && element.Height > 0)
                return element.Height;
            return Math.Max(0, element.MinHeight);
        }
    }
}
