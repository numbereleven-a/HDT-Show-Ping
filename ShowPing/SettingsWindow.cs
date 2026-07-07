using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace ShowPing
{
    internal sealed class SettingsWindow : Window
    {
        private readonly CheckBox showServerPingCheckBox;
        private readonly CheckBox showPacketLossCheckBox;
        private TextBox intervalTextBox;
        private Slider textScaleSlider;
        private Slider opacitySlider;
        private TextBlock textScaleValueTextBlock;
        private TextBlock opacityValueTextBlock;
        private readonly TextBlock statusTextBlock;
        private readonly Action<ShowPingSettings> applySettings;

        public SettingsWindow(ShowPingSettings settings, Action<ShowPingSettings> applySettings)
        {
            this.applySettings = applySettings;
            ResultSettings = settings.Clone();

            Title = "ShowPing";
            Width = 420;
            Height = 292;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            SetSafeOwner();

            var root = new Grid
            {
                Margin = new Thickness(16)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var stack = new StackPanel();
            Grid.SetRow(stack, 0);

            showServerPingCheckBox = CreateCheckBox("Show server ping", ResultSettings.ShowServerPing);
            showPacketLossCheckBox = CreateCheckBox("Show failed checks", ResultSettings.ShowPacketLoss);

            stack.Children.Add(showServerPingCheckBox);
            stack.Children.Add(showPacketLossCheckBox);
            stack.Children.Add(CreateScaleRow());
            stack.Children.Add(CreateOpacityRow());
            stack.Children.Add(CreateIntervalRow());

            statusTextBlock = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(statusTextBlock);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttons, 2);

            var applyButton = new Button { Content = "Apply", Width = 78, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            applyButton.Click += ApplyButton_Click;
            var okButton = new Button { Content = "OK", Width = 78, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            okButton.Click += OkButton_Click;
            var cancelButton = new Button { Content = "Cancel", Width = 78, Height = 28, IsCancel = true };
            buttons.Children.Add(applyButton);
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            root.Children.Add(stack);
            root.Children.Add(buttons);
            Content = root;
        }

        public ShowPingSettings ResultSettings { get; private set; }

        private void SetSafeOwner()
        {
            var owner = Application.Current?.MainWindow;
            if (owner == null || owner == this)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            try
            {
                if (owner.IsVisible || new WindowInteropHelper(owner).Handle != IntPtr.Zero)
                {
                    Owner = owner;
                    WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    return;
                }
            }
            catch
            {
            }

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private static CheckBox CreateCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private FrameworkElement CreateScaleRow()
        {
            var panel = new Grid
            {
                Margin = new Thickness(0, 2, 0, 8)
            };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "Text scale:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(label, 0);
            panel.Children.Add(label);

            textScaleSlider = new Slider
            {
                Minimum = 75,
                Maximum = 150,
                TickFrequency = 5,
                IsSnapToTickEnabled = true,
                Value = ResultSettings.TextScalePercent,
                VerticalAlignment = VerticalAlignment.Center
            };
            textScaleSlider.ValueChanged += (sender, args) => UpdateScaleValueText();
            Grid.SetColumn(textScaleSlider, 1);
            panel.Children.Add(textScaleSlider);

            textScaleValueTextBlock = new TextBlock
            {
                Width = 46,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(textScaleValueTextBlock, 2);
            panel.Children.Add(textScaleValueTextBlock);
            UpdateScaleValueText();

            return panel;
        }

        private FrameworkElement CreateOpacityRow()
        {
            var panel = new Grid
            {
                Margin = new Thickness(0, 2, 0, 8)
            };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "Overlay opacity:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(label, 0);
            panel.Children.Add(label);

            opacitySlider = new Slider
            {
                Minimum = 10,
                Maximum = 100,
                TickFrequency = 5,
                IsSnapToTickEnabled = true,
                Value = ResultSettings.OverlayOpacityPercent,
                VerticalAlignment = VerticalAlignment.Center
            };
            opacitySlider.ValueChanged += (sender, args) => UpdateOpacityValueText();
            Grid.SetColumn(opacitySlider, 1);
            panel.Children.Add(opacitySlider);

            opacityValueTextBlock = new TextBlock
            {
                Width = 46,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(opacityValueTextBlock, 2);
            panel.Children.Add(opacityValueTextBlock);
            UpdateOpacityValueText();

            return panel;
        }

        private FrameworkElement CreateIntervalRow()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(new TextBlock
            {
                Text = "Check every, sec:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            intervalTextBox = new TextBox
            {
                Text = ResultSettings.CheckIntervalSeconds.ToString(),
                Width = 46,
                Height = 24
            };
            panel.Children.Add(intervalTextBox);
            panel.Children.Add(new TextBlock
            {
                Text = "2-10; default 2",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });

            return panel;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ReadSettingsFromUi();
            applySettings(ResultSettings);
            statusTextBlock.Text = "Settings applied.";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ReadSettingsFromUi();
            DialogResult = true;
            Close();
        }

        private void ReadSettingsFromUi()
        {
            int interval;
            if (!int.TryParse(intervalTextBox.Text, out interval))
                interval = 2;

            ResultSettings.ShowServerPing = showServerPingCheckBox.IsChecked == true;
            ResultSettings.ShowPacketLoss = showPacketLossCheckBox.IsChecked == true;
            ResultSettings.TextScalePercent = (int)Math.Round(textScaleSlider.Value);
            ResultSettings.OverlayOpacityPercent = (int)Math.Round(opacitySlider.Value);
            ResultSettings.CheckIntervalSeconds = interval;
            ResultSettings.Normalize();
            intervalTextBox.Text = ResultSettings.CheckIntervalSeconds.ToString();
        }

        private void UpdateScaleValueText()
        {
            if (textScaleValueTextBlock != null && textScaleSlider != null)
                textScaleValueTextBlock.Text = ((int)Math.Round(textScaleSlider.Value)) + "%";
        }

        private void UpdateOpacityValueText()
        {
            if (opacityValueTextBlock != null && opacitySlider != null)
                opacityValueTextBlock.Text = ((int)Math.Round(opacitySlider.Value)) + "%";
        }
    }
}
