using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

// Autodesk.Revit.DB 全域 using（Nice3point SDK 產生的 GlobalUsings）也有 Color／Control／
// Visibility，這個檔案只處理 WPF 外觀，明確 alias 避免 CS0104 模稜兩可參考。
using WpfColor = System.Windows.Media.Color;
using WpfControl = System.Windows.Controls.Control;

namespace RevitMCP.UI
{
    /// <summary>
    /// 相機管理停靠面板的唯一色彩/字型/間距正本。所有子元件（List/Detail/KeyPlan/Presets/
    /// MultiView/PillTabControl/PanelControl）一律透過這裡的常數與 style 工廠方法取色，
    /// 不在各自檔案內硬寫顏色字串 —— 換色盤只改這一個檔案。
    ///
    /// 2026-09-24 決策：面板固定用淺色（不跟隨 Revit UIThemeManager 深色主題）。使用者
    /// 從三份 mockup 選了 B「天空藍卡片」，數值如下逐字對應使用者訊息，不是自行調整。
    /// 根元件（<see cref="CameraManagerPanelControl"/>）必須顯式設定 Background —— 這是
    /// 「整個面板背景黑掉、文字看不到」那個 bug 的根因（Revit 停靠面板宿主在透明 WPF 背後畫黑），
    /// 修法就是不要讓任何一層留白/透明，全部走這裡定義的不透明 Brush。
    /// </summary>
    internal static class CameraManagerTheme
    {
        // ── 色彩（十六進位逐字對應使用者拍板的 mockup B） ──
        internal static readonly WpfColor BackgroundColor = ParseColor("#F3F7FC");
        internal static readonly WpfColor SurfaceColor = ParseColor("#FFFFFF");
        internal static readonly WpfColor BorderColor = ParseColor("#DCE6F2");
        internal static readonly WpfColor GridDividerColor = ParseColor("#EDF2F8");
        internal static readonly WpfColor TextColor = ParseColor("#1C2B3A");
        internal static readonly WpfColor SecondaryTextColor = ParseColor("#6A7B8E");
        internal static readonly WpfColor AccentColor = ParseColor("#3B82C4");
        internal static readonly WpfColor SelectedRowTintColor = ParseColor("#E8F1FB");
        internal static readonly WpfColor GridHeaderColor = ParseColor("#F7FAFD");
        internal static readonly WpfColor WarningColor = ParseColor("#E8A33D");
        internal static readonly WpfColor OkColor = ParseColor("#4CAF7A");
        internal static readonly WpfColor ErrorColor = ParseColor("#D9534F");
        internal static readonly WpfColor StatusBarColor = ParseColor("#F3F7FC");
        internal static readonly WpfColor PaneTitleColor = ParseColor("#FFFFFF");

        internal static readonly Brush Background = Freeze(new SolidColorBrush(BackgroundColor));
        internal static readonly Brush Surface = Freeze(new SolidColorBrush(SurfaceColor));
        internal static readonly Brush BorderBrush = Freeze(new SolidColorBrush(BorderColor));
        internal static readonly Brush GridDivider = Freeze(new SolidColorBrush(GridDividerColor));
        internal static readonly Brush Text = Freeze(new SolidColorBrush(TextColor));
        internal static readonly Brush SecondaryText = Freeze(new SolidColorBrush(SecondaryTextColor));
        internal static readonly Brush Accent = Freeze(new SolidColorBrush(AccentColor));
        internal static readonly Brush SelectedRowTint = Freeze(new SolidColorBrush(SelectedRowTintColor));
        internal static readonly Brush GridHeader = Freeze(new SolidColorBrush(GridHeaderColor));
        internal static readonly Brush Warning = Freeze(new SolidColorBrush(WarningColor));
        internal static readonly Brush Ok = Freeze(new SolidColorBrush(OkColor));
        internal static readonly Brush Error = Freeze(new SolidColorBrush(ErrorColor));
        internal static readonly Brush StatusBarBackground = Freeze(new SolidColorBrush(StatusBarColor));
        internal static readonly Brush PaneTitleBackground = Freeze(new SolidColorBrush(PaneTitleColor));
        internal static readonly Brush WhiteText = Freeze(new SolidColorBrush(Colors.White));
        internal static readonly Brush Transparent = Brushes.Transparent;

        // ── 字型與尺寸 ──
        internal static readonly FontFamily UiFont = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        internal const double BodyFontSize = 12.0;
        internal const double SecondaryFontSize = 11.0;
        internal const double SectionTitleFontSize = 13.0;

        // ── 間距與圓角 ──
        internal const double Spacing = 8.0;
        internal const double CornerRadiusPx = 10.0;

        private static WpfColor ParseColor(string hex) => (WpfColor)ColorConverter.ConvertFromString(hex);

        private static Brush Freeze(SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        /// <summary>卡片外框：白底、圓角、細邊、淡淡的天空藍陰影（blur≈4px, opacity≈0.10）。</summary>
        internal static Border CardWrap(UIElement content, Thickness? margin = null)
        {
            Border border = new Border
            {
                Background = Surface,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(CornerRadiusPx),
                Padding = new Thickness(Spacing),
                Margin = margin ?? new Thickness(0, 0, 0, Spacing),
                Effect = CardShadow(),
                Child = content
            };
            return border;
        }

        internal static DropShadowEffect CardShadow() => new DropShadowEffect
        {
            Color = AccentColor,
            BlurRadius = 4,
            ShadowDepth = 1,
            Direction = 270,
            Opacity = 0.10,
            RenderingBias = RenderingBias.Performance
        };

        internal static TextBlock SectionTitle(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = SectionTitleFontSize,
            Foreground = Text,
            FontFamily = UiFont,
            Margin = new Thickness(0, 0, 0, Spacing / 2)
        };

        /// <summary>2026-09-24：面板各區塊的「區名」小標（① 工具列 … ⑤ 人視高預設值），讓使用者
        /// 能用名稱指稱區塊。刻意比 <see cref="SectionTitle"/> 小（11px semibold、次要文字色、
        /// 下邊距 3px、不加卡片），面板維持不出現外層捲軸。</summary>
        internal const double AreaTitleFontSize = 11.0;

        internal static TextBlock AreaTitle(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = AreaTitleFontSize,
            Foreground = SecondaryText,
            FontFamily = UiFont,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(2, 0, 0, 3)
        };

        internal static TextBlock FieldLabel(string text) => new TextBlock
        {
            Text = text,
            FontSize = BodyFontSize,
            Foreground = Text,
            FontFamily = UiFont,
            VerticalAlignment = VerticalAlignment.Center
        };

        internal static TextBlock Caption(string text) => new TextBlock
        {
            Text = text,
            FontSize = SecondaryFontSize,
            Foreground = SecondaryText,
            FontFamily = UiFont,
            TextWrapping = TextWrapping.Wrap
        };

        /// <summary>主要按鈕：accent 底、白色 semibold 字。</summary>
        internal static Button PrimaryButton(string text)
        {
            Button b = new Button
            {
                Content = text,
                Padding = new Thickness(12, 5, 12, 5),
                Background = Accent,
                Foreground = WhiteText,
                BorderThickness = new Thickness(0),
                FontFamily = UiFont,
                FontSize = BodyFontSize,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ApplyRoundedButtonTemplate(b);
            return b;
        }

        /// <summary>次要按鈕：白底、淺邊框、深色文字。</summary>
        internal static Button SecondaryButton(string text)
        {
            Button b = new Button
            {
                Content = text,
                Padding = new Thickness(10, 4, 10, 4),
                Background = Surface,
                Foreground = Text,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                FontFamily = UiFont,
                FontSize = BodyFontSize,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ApplyRoundedButtonTemplate(b);
            return b;
        }

        /// <summary>圖示風按鈕（標題列 重新整理／從透視圖同步）：小、次要色。</summary>
        internal static Button IconButton(string text, string tooltip)
        {
            Button b = SecondaryButton(text);
            b.Padding = new Thickness(8, 3, 8, 3);
            b.FontSize = SecondaryFontSize;
            b.ToolTip = tooltip;
            return b;
        }

        /// <summary>用 ControlTemplate 把按鈕四角改圓（10px），取代 WPF 預設方角按鈕外觀。</summary>
        private static void ApplyRoundedButtonTemplate(Button b)
        {
            FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(CornerRadiusPx));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            FrameworkElementFactory contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.AppendChild(contentFactory);

            ControlTemplate template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };

            Trigger hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
            template.Triggers.Add(hoverTrigger);

            Trigger disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            template.Triggers.Add(disabledTrigger);

            b.Template = template;
        }

        /// <summary>套用共用 DataGrid 外觀：淺灰分隔線、標題列底色、選取列淡藍、明確前景色。</summary>
        internal static void StyleDataGrid(DataGrid grid)
        {
            grid.FontFamily = UiFont;
            grid.FontSize = BodyFontSize;
            grid.Background = Surface;
            grid.Foreground = Text;
            grid.BorderThickness = new Thickness(0);
            grid.RowBackground = Surface;
            grid.AlternatingRowBackground = Surface;
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.HorizontalGridLinesBrush = GridDivider;
            grid.VerticalGridLinesBrush = GridDivider;
            grid.RowHeight = 26;
            grid.ColumnHeaderHeight = 28;
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;

            Style headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(WpfControl.BackgroundProperty, GridHeader));
            headerStyle.Setters.Add(new Setter(WpfControl.ForegroundProperty, SecondaryText));
            headerStyle.Setters.Add(new Setter(WpfControl.FontFamilyProperty, UiFont));
            headerStyle.Setters.Add(new Setter(WpfControl.FontSizeProperty, SecondaryFontSize));
            headerStyle.Setters.Add(new Setter(WpfControl.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(WpfControl.BorderBrushProperty, GridDivider));
            headerStyle.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(6, 4, 6, 4)));
            grid.ColumnHeaderStyle = headerStyle;

            Style rowStyle = new Style(typeof(DataGridRow));
            Trigger selectedTrigger = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(WpfControl.BackgroundProperty, SelectedRowTint));
            selectedTrigger.Setters.Add(new Setter(WpfControl.ForegroundProperty, Text));
            rowStyle.Triggers.Add(selectedTrigger);
            grid.RowStyle = rowStyle;

            Style cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(6, 2, 6, 2)));
            Trigger editingTrigger = new Trigger { Property = DataGridCell.IsEditingProperty, Value = true };
            editingTrigger.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Accent));
            editingTrigger.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(2)));
            cellStyle.Triggers.Add(editingTrigger);
            grid.CellStyle = cellStyle;
        }

        /// <summary>
        /// 2026-09-25（第三次修「清單數字改不了」）：相機清單常駐可編輯儲存格用的 TextBox 樣式——不再走
        /// DataGrid 編輯模式，而是儲存格內一直是一個 TextBox。平常無邊框、透明底（看起來像純文字），滑鼠
        /// 移上去出現淡邊框，取得鍵盤焦點時白底＋accent 邊框。自訂 ControlTemplate（Border＋PART_ContentHost），
        /// 避免 Aero2 預設樣板的 hover/focus 藍框蓋掉主題色。TargetType 必須是 TextBox（曾因 TargetType
        /// 不符導致 Revit 當掉）。
        /// </summary>
        internal static Style GridCellTextBoxStyle()
        {
            Style s = new Style(typeof(TextBox));
            s.Setters.Add(new Setter(WpfControl.FontFamilyProperty, UiFont));
            s.Setters.Add(new Setter(WpfControl.FontSizeProperty, BodyFontSize));
            s.Setters.Add(new Setter(WpfControl.ForegroundProperty, Text));
            s.Setters.Add(new Setter(WpfControl.BackgroundProperty, Transparent));
            s.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Transparent));
            s.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(2, 0, 2, 0)));
            s.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Right));
            s.Setters.Add(new Setter(WpfControl.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            s.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch));
            s.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.IBeam));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            FrameworkElementFactory host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(UIElement.FocusableProperty, false);
            host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            host.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.AppendChild(host);
            s.Setters.Add(new Setter(WpfControl.TemplateProperty, new ControlTemplate(typeof(TextBox)) { VisualTree = border }));

            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(WpfControl.BorderBrushProperty, BorderBrush));
            hover.Setters.Add(new Setter(WpfControl.BackgroundProperty, Surface));
            s.Triggers.Add(hover);

            Trigger focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Accent));
            focus.Setters.Add(new Setter(WpfControl.BackgroundProperty, Surface));
            s.Triggers.Add(focus);

            Trigger readOnly = new Trigger { Property = TextBox.IsReadOnlyProperty, Value = true };
            readOnly.Setters.Add(new Setter(WpfControl.ForegroundProperty, SecondaryText));
            readOnly.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Arrow));
            s.Triggers.Add(readOnly);
            return s;
        }

        /// <summary>相機清單「樓層」欄常駐 ComboBox 的樣式（TargetType＝ComboBox）：透明底／透明邊框、主題字型
        /// 與前景色；hover 外觀沿用系統樣板。</summary>
        internal static Style GridCellComboBoxStyle()
        {
            Style s = new Style(typeof(ComboBox));
            s.Setters.Add(new Setter(WpfControl.FontFamilyProperty, UiFont));
            s.Setters.Add(new Setter(WpfControl.FontSizeProperty, BodyFontSize));
            s.Setters.Add(new Setter(WpfControl.ForegroundProperty, Text));
            s.Setters.Add(new Setter(WpfControl.BackgroundProperty, Transparent));
            s.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Transparent));
            s.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(3, 1, 2, 1)));
            s.Setters.Add(new Setter(WpfControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            s.Setters.Add(new Setter(WpfControl.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            s.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch));
            s.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            return s;
        }

        /// <summary>細節/清單編輯用的文字輸入框外觀：淡邊框、10px 圓角、focus 時 2px accent 外框。</summary>
        internal static void StyleTextBox(TextBox box)
        {
            box.FontFamily = UiFont;
            box.FontSize = BodyFontSize;
            box.Foreground = Text;
            box.Background = Surface;
            box.BorderBrush = BorderBrush;
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(6, 3, 6, 3);
        }
    }
}
