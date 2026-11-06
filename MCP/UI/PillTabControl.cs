using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

using WpfGrid = System.Windows.Controls.Grid;
using WpfVisibility = System.Windows.Visibility;

namespace RevitMCP.UI
{
    /// <summary>
    /// 「藥丸形」分頁條：不用 WPF 內建 <see cref="TabControl"/>（預設是有立體邊框的舊式外觀，
    /// 使用者明確要求不要），改用一排 <see cref="Button"/>（圓角、選取時 accent 底＋白字，未選取
    /// 透明底＋次要文字色，中間 6px 間距）切換下方內容區的可見性。也是「不要出現外層滾輪」需求的
    /// 一部分——內容區直接鋪滿剩餘空間，捲動與否交給各分頁自己的內容決定（目前只有 Tab1 相機清單
    /// 的 DataGrid 需要捲動）。
    /// </summary>
    internal class PillTabControl : UserControl
    {
        private readonly StackPanel _tabStrip;
        private readonly WpfGrid _contentHost;
        private readonly List<(Button button, FrameworkElement content)> _tabs = new List<(Button, FrameworkElement)>();
        private int _selectedIndex = -1;

        public event Action<int> SelectionChanged;

        public PillTabControl()
        {
            WpfGrid root = new WpfGrid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _tabStrip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, CameraManagerTheme.Spacing)
            };
            WpfGrid.SetRow(_tabStrip, 0);
            root.Children.Add(_tabStrip);

            _contentHost = new WpfGrid();
            WpfGrid.SetRow(_contentHost, 1);
            root.Children.Add(_contentHost);

            Content = root;
        }

        public void AddTab(string header, FrameworkElement content)
        {
            Button button = new Button
            {
                Content = header,
                Padding = new Thickness(14, 5, 14, 5),
                Margin = new Thickness(_tabs.Count == 0 ? 0 : 6, 0, 0, 0),
                BorderThickness = new Thickness(0),
                FontFamily = CameraManagerTheme.UiFont,
                FontSize = CameraManagerTheme.BodyFontSize,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = _tabs.Count
            };
            ApplyPillTemplate(button);
            button.Click += (s, e) => Select((int)button.Tag);
            _tabStrip.Children.Add(button);

            content.Visibility = WpfVisibility.Collapsed;
            _contentHost.Children.Add(content);

            _tabs.Add((button, content));
            if (_selectedIndex < 0) Select(0);
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            _selectedIndex = index;
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool selected = i == index;
                _tabs[i].content.Visibility = selected ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                UpdatePillLook(_tabs[i].button, selected);
            }
            SelectionChanged?.Invoke(index);
        }

        private static void UpdatePillLook(Button button, bool selected)
        {
            button.Background = selected ? CameraManagerTheme.Accent : CameraManagerTheme.Transparent;
            button.Foreground = selected ? CameraManagerTheme.WhiteText : CameraManagerTheme.SecondaryText;
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        /// <summary>藥丸形（完全圓角）模板，取代預設 TabItem/Button 外觀；不畫底線、不畫外框。</summary>
        private static void ApplyPillTemplate(Button button)
        {
            FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            FrameworkElementFactory contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.AppendChild(contentFactory);

            ControlTemplate template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };
            button.Template = template;
            UpdatePillLook(button, false);
        }
    }
}
