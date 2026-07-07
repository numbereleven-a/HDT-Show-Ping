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

        public NetworkOverlayControl()
        {
            pingTextBlock = CreateTextBlock(13);
            pingTextBlock.Text = "PING: --";

            lossTextBlock = CreateTextBlock(12);
            lossTextBlock.Text = "CHECK FAIL: --";

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stack.Children.Add(pingTextBlock);
            stack.Children.Add(lossTextBlock);

            BorderThickness = new Thickness(1);
            Padding = new Thickness(5, 2, 5, 2);
            Child = stack;
        }

        public void ApplySettings(ShowPingSettings settings)
        {
            var scale = settings.TextScalePercent / 100.0;
            pingTextBlock.FontSize = 13 * scale;
            lossTextBlock.FontSize = 12 * scale;

            Width = double.NaN;
            Height = double.NaN;
            MinWidth = 110 * scale;
            MinHeight = settings.ShowPacketLoss ? 30 * scale : 18 * scale;

            var alpha = (byte)Math.Round(255 * settings.OverlayOpacityPercent / 100.0);
            BorderBrush = new SolidColorBrush(Color.FromArgb(alpha, 0x14, 0x16, 0x17));
            Background = new SolidColorBrush(Color.FromArgb(alpha, 0x23, 0x27, 0x2A));
        }

        public void SetNetworkState(NetworkSnapshot snapshot, bool showPacketLoss)
        {
            pingTextBlock.Text = snapshot.PingText;
            pingTextBlock.Foreground = snapshot.Brush;
            lossTextBlock.Text = snapshot.LossText;
            lossTextBlock.Foreground = snapshot.Brush;
            lossTextBlock.Visibility = showPacketLoss ? Visibility.Visible : Visibility.Collapsed;
            ToolTip = string.IsNullOrWhiteSpace(snapshot.Endpoint)
                ? null
                : "Endpoint: " + snapshot.Endpoint;
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
