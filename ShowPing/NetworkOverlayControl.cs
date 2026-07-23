using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;

namespace ShowPing
{
    internal sealed class NetworkOverlayControl : Border
    {
        private readonly TextBlock pingTextBlock;
        private readonly TextBlock lossTextBlock;
        private readonly TextBlock compactRegionTextBlock;
        private readonly TextBlock regionTextBlock;
        private readonly TextBlock endpointTextBlock;

        public double ReservedPlacementWidth { get; private set; }
        public double ReservedPlacementHeight { get; private set; }

        public NetworkOverlayControl()
        {
            pingTextBlock = CreateTextBlock(13);
            pingTextBlock.Text = "PING: --";

            lossTextBlock = CreateTextBlock(12);
            lossTextBlock.Text = "CHECK FAIL: --";
            compactRegionTextBlock = CreateTextBlock(13);
            compactRegionTextBlock.Text = "";
            regionTextBlock = CreateTextBlock(13);
            regionTextBlock.Text = "";
            endpointTextBlock = CreateTextBlock(13);
            endpointTextBlock.Text = "";

            var primaryRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            primaryRow.Children.Add(pingTextBlock);
            primaryRow.Children.Add(lossTextBlock);
            primaryRow.Children.Add(compactRegionTextBlock);

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stack.Children.Add(primaryRow);
            stack.Children.Add(regionTextBlock);
            stack.Children.Add(endpointTextBlock);

            BorderThickness = new Thickness(1);
            Padding = new Thickness(5, 2, 5, 2);
            Child = stack;
        }

        public void ApplySettings(ShowPingSettings settings)
        {
            var scale = settings.TextScalePercent / 100.0;
            pingTextBlock.FontSize = 13 * scale;
            lossTextBlock.FontSize = 13 * scale;
            compactRegionTextBlock.FontSize = 13 * scale;
            regionTextBlock.FontSize = 13 * scale;
            endpointTextBlock.FontSize = 13 * scale;

            var fontWeight = GetFontWeight(settings.FontWeightMode);
            pingTextBlock.FontWeight = fontWeight;
            lossTextBlock.FontWeight = fontWeight;
            compactRegionTextBlock.FontWeight = fontWeight;
            regionTextBlock.FontWeight = fontWeight;
            endpointTextBlock.FontWeight = fontWeight;

            Width = double.NaN;
            Height = double.NaN;
            MinWidth = GetMinWidth(settings) * scale;
            MinHeight = GetMinHeight(settings) * scale;
            ReservedPlacementWidth = GetReservedPlacementWidth(settings, scale, fontWeight);
            ReservedPlacementHeight = MinHeight;

            var alpha = (byte)Math.Round(255 * settings.OverlayOpacityPercent / 100.0);
            BorderBrush = new SolidColorBrush(Color.FromArgb(alpha, 0x14, 0x16, 0x17));
            Background = new SolidColorBrush(Color.FromArgb(alpha, 0x23, 0x27, 0x2A));
        }

        public void SetNetworkState(NetworkSnapshot snapshot, ShowPingSettings settings, string regionOverride = null)
        {
            var endpointText = GetEndpointText(snapshot, settings);
            var regionText = GetRegionText(settings, regionOverride);
            var showLoss = settings.ShowPacketLoss
                && (!settings.OnlyShowFailedChecksWhenDetected || snapshot.FailurePercent > 0);

            pingTextBlock.Text = settings.CompactMode ? "PING " + snapshot.PingValue : snapshot.PingText;
            pingTextBlock.Foreground = snapshot.Brush;
            lossTextBlock.Text = settings.CompactMode
                ? " \u00b7 FAIL " + snapshot.LossValue
                : "   CHECK FAIL: " + snapshot.LossValue;
            lossTextBlock.Foreground = GetFailureBrush(snapshot.FailurePercent);
            lossTextBlock.Visibility = showLoss ? Visibility.Visible : Visibility.Collapsed;
            compactRegionTextBlock.Text = settings.CompactMode && regionText != null ? " \u00b7 " + regionText : "";
            compactRegionTextBlock.Foreground = snapshot.Brush;
            compactRegionTextBlock.Visibility = settings.CompactMode && regionText != null
                ? Visibility.Visible
                : Visibility.Collapsed;
            regionTextBlock.Text = settings.CompactMode || regionText == null ? "" : "REGION: " + regionText;
            regionTextBlock.Foreground = snapshot.Brush;
            regionTextBlock.Visibility = settings.CompactMode || regionText == null ? Visibility.Collapsed : Visibility.Visible;
            endpointTextBlock.Text = endpointText ?? "";
            endpointTextBlock.Foreground = snapshot.Brush;
            endpointTextBlock.Visibility = string.IsNullOrWhiteSpace(endpointText) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static Brush GetFailureBrush(int failurePercent)
        {
            if (failurePercent < 0)
                return Brushes.Gray;
            if (failurePercent >= 30)
                return Brushes.Red;
            if (failurePercent >= 10)
                return Brushes.Goldenrod;
            return Brushes.ForestGreen;
        }

        private static string GetRegionText(ShowPingSettings settings, string regionOverride)
        {
            if (!settings.ShowRegion)
                return null;
            if (!string.IsNullOrWhiteSpace(regionOverride))
                return regionOverride;

            var game = Core.Game;
            if (game == null)
                return null;

            switch (game.CurrentRegion)
            {
                case Region.US:
                    return "USA";
                case Region.EU:
                    return "EU";
                case Region.ASIA:
                    return "ASIA";
                case Region.CHINA:
                    return "CHINA";
                default:
                    return null;
            }
        }

        private static string GetEndpointText(NetworkSnapshot snapshot, ShowPingSettings settings)
        {
            return settings.ShowEndpointIp ? snapshot.EndpointIp ?? snapshot.Endpoint : null;
        }

        private static double GetMinWidth(ShowPingSettings settings)
        {
            if (settings.CompactMode)
                return 80;
            return settings.ShowEndpointIp ? 150 : 110;
        }

        private static double GetMinHeight(ShowPingSettings settings)
        {
            var lines = 1;
            if (!settings.CompactMode && settings.ShowRegion)
                lines++;
            if (settings.ShowEndpointIp)
                lines++;
            return Math.Max(18, 6 + lines * 14);
        }

        private double GetReservedPlacementWidth(
            ShowPingSettings settings,
            double scale,
            FontWeight fontWeight)
        {
            var primaryText = settings.CompactMode ? "PING 9999 ms" : "PING: 9999 ms";
            if (settings.ShowPacketLoss)
            {
                primaryText += settings.CompactMode
                    ? " \u00b7 FAIL 100%"
                    : "   CHECK FAIL: 100%";
            }
            if (settings.CompactMode && settings.ShowRegion)
                primaryText += " \u00b7 CHINA";

            var width = MeasureTextWidth(primaryText, 13 * scale, fontWeight);
            if (!settings.CompactMode && settings.ShowRegion)
                width = Math.Max(width, MeasureTextWidth("REGION: CHINA", 13 * scale, fontWeight));
            if (settings.ShowEndpointIp)
                width = Math.Max(width, MeasureTextWidth("255.255.255.255:65535", 13 * scale, fontWeight));

            return Math.Max(MinWidth, width + Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right);
        }

        private double MeasureTextWidth(string text, double fontSize, FontWeight fontWeight)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = pingTextBlock.FontFamily,
                FontSize = fontSize,
                FontWeight = fontWeight,
                TextWrapping = TextWrapping.NoWrap
            };
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return textBlock.DesiredSize.Width;
        }

        private static FontWeight GetFontWeight(int mode)
        {
            switch (mode)
            {
                case 0:
                    return FontWeights.Normal;
                case 2:
                    return FontWeights.Bold;
                case 3:
                    return FontWeights.ExtraBold;
                default:
                    return FontWeights.SemiBold;
            }
        }

        private static TextBlock CreateTextBlock(double fontSize)
        {
            return new TextBlock
            {
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 1,
                    ShadowDepth = 1
                }
            };
        }

    }
}
