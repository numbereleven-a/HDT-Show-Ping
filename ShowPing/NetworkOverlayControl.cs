using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ShowPing
{
    internal sealed class NetworkOverlayControl : Border
    {
        private readonly TextBlock pingTextBlock;
        private readonly TextBlock lossTextBlock;
        private readonly TextBlock endpointTextBlock;

        public NetworkOverlayControl()
        {
            pingTextBlock = CreateTextBlock(13);
            pingTextBlock.Text = "PING: --";

            lossTextBlock = CreateTextBlock(12);
            lossTextBlock.Text = "CHECK FAIL: --";
            endpointTextBlock = CreateTextBlock(13);
            endpointTextBlock.Text = "";

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stack.Children.Add(pingTextBlock);
            stack.Children.Add(lossTextBlock);
            stack.Children.Add(endpointTextBlock);

            BorderThickness = new Thickness(1);
            Padding = new Thickness(5, 2, 5, 2);
            Child = stack;
        }

        public void ApplySettings(ShowPingSettings settings)
        {
            var scale = settings.TextScalePercent / 100.0;
            pingTextBlock.FontSize = 13 * scale;
            lossTextBlock.FontSize = 12 * scale;
            endpointTextBlock.FontSize = 13 * scale;

            var fontWeight = GetFontWeight(settings.FontWeightMode);
            pingTextBlock.FontWeight = fontWeight;
            lossTextBlock.FontWeight = fontWeight;
            endpointTextBlock.FontWeight = fontWeight;

            Width = double.NaN;
            Height = double.NaN;
            MinWidth = GetMinWidth(settings) * scale;
            MinHeight = GetMinHeight(settings) * scale;

            var alpha = (byte)Math.Round(255 * settings.OverlayOpacityPercent / 100.0);
            BorderBrush = new SolidColorBrush(Color.FromArgb(alpha, 0x14, 0x16, 0x17));
            Background = new SolidColorBrush(Color.FromArgb(alpha, 0x23, 0x27, 0x2A));
        }

        public void SetNetworkState(NetworkSnapshot snapshot, ShowPingSettings settings)
        {
            var endpointText = GetEndpointText(snapshot, settings);

            pingTextBlock.Text = settings.CompactMode
                ? GetCompactText(snapshot, settings)
                : snapshot.PingText;
            pingTextBlock.Foreground = snapshot.Brush;
            lossTextBlock.Text = snapshot.LossText;
            lossTextBlock.Foreground = snapshot.Brush;
            lossTextBlock.Visibility = !settings.CompactMode && settings.ShowPacketLoss ? Visibility.Visible : Visibility.Collapsed;
            endpointTextBlock.Text = endpointText ?? "";
            endpointTextBlock.Foreground = snapshot.Brush;
            endpointTextBlock.Visibility = string.IsNullOrWhiteSpace(endpointText) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string GetCompactText(NetworkSnapshot snapshot, ShowPingSettings settings)
        {
            if (!settings.ShowPacketLoss)
                return "PING " + snapshot.PingValue;
            return "PING " + snapshot.PingValue + " \u00b7 FAIL " + snapshot.LossValue;
        }

        private static string GetEndpointText(NetworkSnapshot snapshot, ShowPingSettings settings)
        {
            return settings.ShowEndpointIp ? snapshot.EndpointIp ?? snapshot.Endpoint : null;
        }

        private static double GetMinWidth(ShowPingSettings settings)
        {
            if (settings.CompactMode)
                return settings.ShowPacketLoss ? 160 : 80;
            return settings.ShowEndpointIp ? 150 : 110;
        }

        private static double GetMinHeight(ShowPingSettings settings)
        {
            var lines = 1;
            if (!settings.CompactMode && settings.ShowPacketLoss)
                lines++;
            if (settings.ShowEndpointIp)
                lines++;
            return Math.Max(18, 6 + lines * 14);
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
