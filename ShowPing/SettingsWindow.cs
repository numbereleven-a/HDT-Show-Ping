using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ShowPing
{
    internal sealed class SettingsWindow : Window
    {
        private readonly CheckBox showServerPingCheckBox;
        private readonly CheckBox showPacketLossCheckBox;
        private readonly CheckBox onlyShowFailedChecksWhenDetectedCheckBox;
        private readonly CheckBox showEndpointIpCheckBox;
        private readonly CheckBox showRegionCheckBox;
        private readonly CheckBox compactModeCheckBox;
        private readonly CheckBox pinNetworkOverlayPositionCheckBox;
        private ComboBox fontWeightComboBox;
        private TextBox intervalTextBox;
        private Slider textScaleSlider;
        private Slider opacitySlider;
        private TextBlock textScaleValueTextBlock;
        private TextBlock opacityValueTextBlock;
        private readonly TextBlock statusTextBlock;
        private readonly TextBlock versionTextBlock;
        private readonly Action<ShowPingSettings> applySettings;
        private readonly Action<ShowPingSettings> previewSettings;
        private readonly Action stopPreview;
        private readonly Button previewButton;
        private bool previewActive;

        public SettingsWindow(
            ShowPingSettings settings,
            Version version,
            Action<ShowPingSettings> applySettings,
            Action<ShowPingSettings> previewSettings,
            Action stopPreview)
        {
            this.applySettings = applySettings;
            this.previewSettings = previewSettings;
            this.stopPreview = stopPreview;
            ResultSettings = settings.Clone();

            Title = "ShowPing";
            Width = 440;
            Height = 560;
            MinWidth = 420;
            MinHeight = 470;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            PreviewKeyDown += SettingsWindow_PreviewKeyDown;
            SetSafeOwner();

            var root = new Grid
            {
                Margin = new Thickness(16)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var stack = new StackPanel();
            var scroller = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(scroller, 0);

            showServerPingCheckBox = CreateCheckBox("Show server ping", ResultSettings.ShowServerPing);
            showPacketLossCheckBox = CreateCheckBox("Show failed checks", ResultSettings.ShowPacketLoss);
            onlyShowFailedChecksWhenDetectedCheckBox = CreateCheckBox(
                "Only show when detected",
                ResultSettings.OnlyShowFailedChecksWhenDetected);
            onlyShowFailedChecksWhenDetectedCheckBox.Margin = new Thickness(20, 0, 0, 8);
            showEndpointIpCheckBox = CreateCheckBox("Show endpoint IP", ResultSettings.ShowEndpointIp);
            showRegionCheckBox = CreateCheckBox("Show region", ResultSettings.ShowRegion);
            compactModeCheckBox = CreateCheckBox("Compact mode", ResultSettings.CompactMode);
            compactModeCheckBox.FontWeight = FontWeights.SemiBold;
            compactModeCheckBox.Margin = new Thickness(0);
            pinNetworkOverlayPositionCheckBox = CreateCheckBox(
                "Pin overlay position",
                ResultSettings.PinNetworkOverlayPosition);
            pinNetworkOverlayPositionCheckBox.FontWeight = FontWeights.SemiBold;
            pinNetworkOverlayPositionCheckBox.Margin = new Thickness(0);

            stack.Children.Add(CreateHighlightedOption(compactModeCheckBox));
            stack.Children.Add(CreateSectionTitle("Displayed data", new Thickness(0, 8, 0, 8)));
            stack.Children.Add(showServerPingCheckBox);
            stack.Children.Add(showPacketLossCheckBox);
            stack.Children.Add(onlyShowFailedChecksWhenDetectedCheckBox);
            stack.Children.Add(showEndpointIpCheckBox);
            stack.Children.Add(showRegionCheckBox);
            stack.Children.Add(CreateSeparatedOption(pinNetworkOverlayPositionCheckBox));

            previewButton = new Button
            {
                Content = "Preview overlay",
                Width = 126,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12)
            };
            previewButton.Click += PreviewButton_Click;
            stack.Children.Add(previewButton);

            stack.Children.Add(CreateSectionTitle("Appearance", new Thickness(0, 4, 0, 8)));
            stack.Children.Add(CreateFontWeightRow());
            stack.Children.Add(CreateScaleRow());
            stack.Children.Add(CreateOpacityRow());
            stack.Children.Add(CreateIntervalRow());

            statusTextBlock = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(statusTextBlock);

            var footer = new Grid
            {
                Margin = new Thickness(0, 10, 0, 0)
            };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(footer, 1);

            versionTextBlock = new TextBlock
            {
                Text = "Version " + version.ToString(2),
                FontSize = 11,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(versionTextBlock, 0);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(buttons, 1);

            var applyButton = new Button { Content = "Apply", Width = 78, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            applyButton.Click += ApplyButton_Click;
            var okButton = new Button { Content = "OK", Width = 78, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            okButton.Click += OkButton_Click;
            var cancelButton = new Button { Content = "Cancel", Width = 78, Height = 28 };
            cancelButton.Click += CancelButton_Click;
            buttons.Children.Add(applyButton);
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            footer.Children.Add(versionTextBlock);
            footer.Children.Add(buttons);

            root.Children.Add(scroller);
            root.Children.Add(footer);
            Content = root;

            showPacketLossCheckBox.Checked += DisplayOption_Changed;
            showPacketLossCheckBox.Unchecked += DisplayOption_Changed;
            showServerPingCheckBox.Checked += DisplayOption_Changed;
            showServerPingCheckBox.Unchecked += DisplayOption_Changed;
            showEndpointIpCheckBox.Checked += DisplayOption_Changed;
            showEndpointIpCheckBox.Unchecked += DisplayOption_Changed;
            showRegionCheckBox.Checked += DisplayOption_Changed;
            showRegionCheckBox.Unchecked += DisplayOption_Changed;
            compactModeCheckBox.Checked += DisplayOption_Changed;
            compactModeCheckBox.Unchecked += DisplayOption_Changed;
            onlyShowFailedChecksWhenDetectedCheckBox.Checked += DisplayOption_Changed;
            onlyShowFailedChecksWhenDetectedCheckBox.Unchecked += DisplayOption_Changed;
            fontWeightComboBox.SelectionChanged += DisplayOption_Changed;
            UpdateDependentControls();
        }

        public ShowPingSettings ResultSettings { get; private set; }
        public bool Accepted { get; private set; }

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

        private static FrameworkElement CreateHighlightedOption(CheckBox checkBox)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(234, 244, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(38, 115, 186)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8, 7, 8, 7),
                Margin = new Thickness(0, 0, 0, 8),
                Child = checkBox
            };
        }

        private static FrameworkElement CreateSectionTitle(string text, Thickness margin)
        {
            return new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DimGray,
                Margin = margin
            };
        }

        private static FrameworkElement CreateSeparatedOption(CheckBox checkBox)
        {
            return new Border
            {
                BorderBrush = SystemColors.ActiveBorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0, 9, 0, 9),
                Margin = new Thickness(0, 4, 0, 12),
                Child = checkBox
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
            textScaleSlider.ValueChanged += (sender, args) =>
            {
                UpdateScaleValueText();
                UpdatePreviewIfActive();
            };
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

        private FrameworkElement CreateFontWeightRow()
        {
            fontWeightComboBox = CreateComboBox(
                new[] { "Normal", "SemiBold", "Bold", "ExtraBold" },
                ResultSettings.FontWeightMode);
            return CreateComboRow("Font weight:", fontWeightComboBox);
        }

        private static ComboBox CreateComboBox(string[] values, int selectedIndex)
        {
            var comboBox = new ComboBox
            {
                Height = 24,
                MinWidth = 140,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (var value in values)
                comboBox.Items.Add(value);

            if (selectedIndex < 0 || selectedIndex >= values.Length)
                selectedIndex = 0;
            comboBox.SelectedIndex = selectedIndex;
            return comboBox;
        }

        private static FrameworkElement CreateComboRow(string labelText, ComboBox comboBox)
        {
            var panel = new Grid
            {
                Margin = new Thickness(0, 2, 0, 8)
            };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(label, 0);
            panel.Children.Add(label);

            Grid.SetColumn(comboBox, 1);
            panel.Children.Add(comboBox);
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
            opacitySlider.ValueChanged += (sender, args) =>
            {
                UpdateOpacityValueText();
                UpdatePreviewIfActive();
            };
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
                Text = "2-10; default 3",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });

            return panel;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ReadSettingsFromUi();
            applySettings(ResultSettings.Clone());
            if (previewActive)
                previewSettings(ResultSettings.Clone());
            statusTextBlock.Text = "Settings applied.";
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (previewActive)
            {
                previewActive = false;
                stopPreview();
                previewButton.Content = "Preview overlay";
                statusTextBlock.Text = "Preview stopped.";
                return;
            }

            ReadSettingsFromUi();
            previewActive = true;
            previewSettings(ResultSettings.Clone());
            previewButton.Content = "Stop preview";
            statusTextBlock.Text = "Preview is movable. Failure states change automatically.";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ReadSettingsFromUi();
            Accepted = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            e.Handled = true;
            Close();
        }

        private void ReadSettingsFromUi()
        {
            int interval;
            if (!int.TryParse(intervalTextBox.Text, out interval))
                interval = ResultSettings.CheckIntervalSeconds;

            ResultSettings.ShowServerPing = showServerPingCheckBox.IsChecked == true;
            ResultSettings.ShowPacketLoss = showPacketLossCheckBox.IsChecked == true;
            ResultSettings.OnlyShowFailedChecksWhenDetected =
                onlyShowFailedChecksWhenDetectedCheckBox.IsChecked == true;
            ResultSettings.ShowEndpointIp = showEndpointIpCheckBox.IsChecked == true;
            ResultSettings.ShowRegion = showRegionCheckBox.IsChecked == true;
            ResultSettings.CompactMode = compactModeCheckBox.IsChecked == true;
            ResultSettings.PinNetworkOverlayPosition = pinNetworkOverlayPositionCheckBox.IsChecked == true;
            ResultSettings.FontWeightMode = fontWeightComboBox.SelectedIndex;
            ResultSettings.TextScalePercent = (int)Math.Round(textScaleSlider.Value);
            ResultSettings.OverlayOpacityPercent = (int)Math.Round(opacitySlider.Value);
            ResultSettings.CheckIntervalSeconds = interval;
            ResultSettings.Normalize();
            intervalTextBox.Text = ResultSettings.CheckIntervalSeconds.ToString();
        }

        private void DisplayOption_Changed(object sender, RoutedEventArgs e)
        {
            UpdateDependentControls();
            UpdatePreviewIfActive();
        }

        private void UpdateDependentControls()
        {
            onlyShowFailedChecksWhenDetectedCheckBox.IsEnabled = showPacketLossCheckBox.IsChecked == true;
        }

        private void UpdatePreviewIfActive()
        {
            if (!previewActive)
                return;

            ReadSettingsFromUi();
            previewSettings(ResultSettings.Clone());
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
