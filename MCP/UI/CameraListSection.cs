using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using RevitMCP.Configuration;
using RevitMCP.Core;

using WpfGrid = System.Windows.Controls.Grid;
using WpfColor = System.Windows.Media.Color;
using WpfBinding = System.Windows.Data.Binding;
// UIElement 繼承鏈上已有實例屬性 Visibility（UIElement.Visibility）／FrameworkElement 沒有
// FormattedText，但 Autodesk.Revit.DB 也有同名的 Visibility／FormattedText 型別——在這個類別
// 的方法內直接寫裸 `Visibility`／`FormattedText` 一律會被解析成錯誤的符號（前者甚至會被解析成
// 「this.Visibility 這個實例屬性」而不是型別，CS0176/CS0120；後者兩個候選型別 CS0104 模稜兩可），
// 一律用別名指到 WPF 版本，不要在本檔任何地方使用裸名稱。
using WpfVisibility = System.Windows.Visibility;
using WpfFormattedText = System.Windows.Media.FormattedText;

namespace RevitMCP.UI
{
    /// <summary>
    /// Tab1「相機」清單。
    ///   v5（2026-09-24）：名稱欄唯讀，（2026-09-26 起）雙擊名稱＝觸發「開啟動作」（面板端預設一鍵 4 視圖）、單擊＝只選取；列右鍵選單
    ///     （一鍵 4 視圖／僅打開透視圖／重新命名…／複製到其他樓層…／重設剖面位置，見 RowContextMenuRequested）。
    ///   v6（2026-09-25，第三次修「數字改不了」）：DataGrid 全唯讀、永不進編輯模式。樓層／離樓板cm／水平角°／
    ///     俯仰角°（與選用的平面 X/Y、遠裁剪）是 DataGridTemplateColumn，儲存格內是常駐 TextBox／ComboBox：
    ///     點一下取得焦點＋全選、打字、Enter 或離開＝提交、Esc＝還原；樓層＝使用者開的下拉關閉時提交。
    ///     提交一律送事件交回 <see cref="CameraManagerPanelControl"/>，由那裡經 ExternalEventManager 開交易寫入
    ///     Revit；本元件完全不碰 Revit API。顯示值由交易完成後的清單重讀帶回。
    ///   預設欄位在 300px 面板寬度下不出現水平捲軸；選用欄位右鍵標題勾選（狀態存 CameraManagerSettings）。
    /// 搜尋框與重新整理／從透視圖同步按鈕在面板常駐標題列，這裡只透過 <see cref="SetSearchFilter"/> 接收關鍵字。
    /// </summary>
    internal class CameraListSection : UserControl
    {
        private readonly DataGrid _grid;
        private List<CameraRow> _allRows = new List<CameraRow>();
        private List<LevelOption> _levels = new List<LevelOption>();
        private string _searchKeyword = "";

        private DataGridTextColumn _nameColumn;
        private DataGridTemplateColumn _levelColumn;
        private DataGridColumn _colPlaneX, _colPlaneY, _colFarClip, _colFov, _colEyeElevation;

        private static readonly Style RightAlignedElementStyle = BuildRightAlignedElementStyle();
        // 儲存格常駐控制項樣式（TargetType 分別為 TextBox／ComboBox；欄位建置前就要可用，所以用欄位初始設定式）。
        private readonly Style _cellTextBoxStyle = CameraManagerTheme.GridCellTextBoxStyle();
        private readonly Style _cellComboStyle = CameraManagerTheme.GridCellComboBoxStyle();

        /// <summary>(選取列, 是否為使用者操作造成)。2026-09-24 第二輪崩潰調查：程式化的選取
        /// （SetRows 換資料來源、SelectByViewId 預設選取）userInitiated=false，面板據此決定
        /// 不做任何會碰 Revit 的副作用（自動切換底圖→ExportImage、讀剖面線）。</summary>
        public event Action<List<CameraRow>, bool> SelectionChanged;

        /// <summary>&gt;0 時代表目前的選取變化是程式造成的（見 SelectionChanged）。</summary>
        private int _programmaticSelection;
        public event Action RefreshRequested;

        /// <summary>2026-09-24 v5：名稱欄改唯讀，單擊直接觸發「開啟動作」（面板端預設一鍵 4
        /// 視圖，見 CameraManagerPanelControl.RunConfiguredOpenAction），取代原本雙擊列跳轉／
        /// 「↗」欄／名稱就地編輯三種手勢。</summary>
        public event Action<CameraRow> RowOpenActionRequested;
        /// <summary>DataGrid 列右鍵：(row, 這一列的 DataGridRow 元素，供選單 PlacementTarget 用)。</summary>
        public event Action<CameraRow, UIElement> RowContextMenuRequested;
        /// <summary>樓層欄選了另一個樓層：(row, 目標樓層 Id)。2026-09-25 起＝複製相機到該樓層，原相機不變。</summary>
        public event Action<CameraRow, ElementId> LevelEditCommitted;
        /// <summary>離樓板cm／水平角／俯仰角欄：(row, 欄位鍵, 新數值)。欄位鍵："Height"/"HAngle"/"Pitch"。</summary>
        public event Action<CameraRow, string, double> NumericFieldEditCommitted;
        /// <summary>選用欄「平面 X/Y cm」：(row, 軸："X"/"Y", 新數值 cm)。</summary>
        public event Action<CameraRow, string, double> PlaneFieldEditCommitted;
        /// <summary>選用欄「遠裁剪 cm」：(row, 新數值 cm)。</summary>
        public event Action<CameraRow, double> FarClipEditCommitted;

        private static CameraManagerSettings Settings => ConfigManager.Instance.Settings.CameraManager;

        public CameraListSection()
        {
            WpfGrid root = new WpfGrid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 0 DataGrid
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 1 操作提示

            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true, // 2026-09-25：DataGrid 永不進編輯模式；可編輯欄是常駐 TextBox/ComboBox
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                FrozenColumnCount = 1, // 名稱欄凍結：面板變窄或選用欄位造成橫向捲動時仍看得到名稱
                RowHeaderWidth = 0
            };
            CameraManagerTheme.StyleDataGrid(_grid);

            BuildColumns();

            _grid.LoadingRow += Grid_LoadingRow;
            _grid.SelectionChanged += Grid_SelectionChanged;
            // 2026-09-25 第三次修正：拿掉 CellEditEnding／RowEditEnding／BeginningEdit／PreparingCellForEdit／
            // PreviewKeyDown／LostKeyboardFocus——DataGrid 不再進編輯模式，編輯由儲存格內常駐控制項處理
            // （見「編輯」區段）。PreviewMouseLeftButtonDown 只剩「雙擊名稱＝開啟動作」（2026-09-26）。
            _grid.PreviewMouseLeftButtonDown += Grid_PreviewMouseLeftButtonDown;
            _grid.PreviewMouseRightButtonDown += Grid_PreviewMouseRightButtonDown;

            WpfGrid.SetRow(_grid, 0);
            root.Children.Add(_grid);

            TextBlock hint = new TextBlock
            {
                // 2026-09-25：改樓層＝複製（不再就地改樓層）。字數比舊版短；300px 下估算放得下（未經實機像素驗證），
                // 仍加省略號＋工具提示作為保險。
                // 2026-09-25 Wave 3：縮短到約 20 字（11 px 字約 220 px），300 px 面板扣掉邊框仍放得下；完整說明在 ToolTip。
                // 2026-09-26 第四輪（使用者決策）：單擊＝只選取；雙擊名稱＝一鍵 4 視圖。橘色列說明放 ToolTip＋① ⋯ 選單圖例（300 px 放不下）。
                Text = "雙擊名稱開 4 視圖・點數字直接修改・右鍵更多",
                ToolTip = "② 相機清單：單擊＝選取（地圖標出位置，不動 Revit）・雙擊名稱＝一鍵 4 視圖・點數字直接修改（Enter 確定、Esc 還原）・改樓層＝在同位置複製一台相機到該樓層・右鍵＝出圖紙、視角版本、重新命名、複製等" +
                          Environment.NewLine + "橘色列＝離樓板高度不在 120–200 cm（可能偏低／偏高）；粗體＋左側藍條＝目前正在看的透視相機",
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = CameraManagerTheme.SecondaryText,
                FontFamily = CameraManagerTheme.UiFont,
                FontSize = CameraManagerTheme.SecondaryFontSize,
                Margin = new Thickness(4, 2, 4, 2)
            };
            WpfGrid.SetRow(hint, 1);
            root.Children.Add(hint);

            Content = root;
        }

        // ══════════════════════════════════════════════════════════
        // 欄位建置
        // ══════════════════════════════════════════════════════════

        private void BuildColumns()
        {
            // 名稱欄唯讀——單擊直接觸發開啟動作（見 Grid_PreviewMouseLeftButtonDown → RowOpenActionRequested），
            // 改名在右鍵選單。
            _nameColumn = new DataGridTextColumn
            {
                Header = "名稱",
                Binding = new WpfBinding(nameof(CameraRow.Name)) { Mode = System.Windows.Data.BindingMode.OneWay },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 45,
                IsReadOnly = true,
                ElementStyle = BuildEllipsisElementStyle()
            };
            _grid.Columns.Add(_nameColumn);

            _levelColumn = BuildLevelColumn();
            try
            {
                // 2026-09-25：樓層欄標題也掛「改樓層＝複製」提示（沿用主題標題樣式）。
                var levelHeaderStyle = new Style(typeof(DataGridColumnHeader), _grid.ColumnHeaderStyle);
                levelHeaderStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, LevelCopyTooltip));
                _levelColumn.HeaderStyle = levelHeaderStyle;
            }
            catch (Exception ex) { Logger.Error("清單：樓層欄標題提示設定失敗", ex); }
            _grid.Columns.Add(_levelColumn);

            // 2026-09-25 第三次修正：可編輯欄一律 DataGridTemplateColumn＋常駐 TextBox（不走 DataGrid 編輯模式）。
            // SortMemberPath 一律指到可比較的原始數值（double／double?），不是顯示字串。
            _grid.Columns.Add(BuildLiveNumericColumn("離樓板 cm", FieldHeight, nameof(CameraRow.HeightAboveLevelCmText), null, 50, nameof(CameraRow.HeightAboveLevelCm)));
            _grid.Columns.Add(BuildLiveNumericColumn("水平角 °", FieldHAngle, nameof(CameraRow.HorizontalAngleDeg), "F1", 50, nameof(CameraRow.HorizontalAngleDeg)));
            _grid.Columns.Add(BuildLiveNumericColumn("俯仰角 °", FieldPitch, nameof(CameraRow.PitchDeg), "F1", 50, nameof(CameraRow.PitchDeg)));

            // ── 選用欄位（預設隱藏，右鍵標題勾選） ──
            _colPlaneX = BuildLiveNumericColumn("平面 X cm", FieldPlaneX, nameof(CameraRow.EyeXCm), "F1", 60, nameof(CameraRow.EyeXCm));
            _grid.Columns.Add(_colPlaneX);

            _colPlaneY = BuildLiveNumericColumn("平面 Y cm", FieldPlaneY, nameof(CameraRow.EyeYCm), "F1", 60, nameof(CameraRow.EyeYCm));
            _grid.Columns.Add(_colPlaneY);

            _colFarClip = BuildLiveNumericColumn("遠裁剪 cm", FieldFarClip, nameof(CameraRow.FarClipCmText), null, 62, nameof(CameraRow.FarClipCm));
            _grid.Columns.Add(_colFarClip);

            _colFov = new DataGridTextColumn
            {
                Header = "視野角 °（估算）",
                Binding = new WpfBinding(nameof(CameraRow.FovEstimateDeg)) { Mode = System.Windows.Data.BindingMode.OneWay, StringFormat = "F0" },
                Width = 100,
                IsReadOnly = true,
                SortMemberPath = nameof(CameraRow.FovEstimateDeg),
                ElementStyle = RightAlignedElementStyle
            };
            _grid.Columns.Add(_colFov);

            TextBlock eyeElevationHeader = new TextBlock
            {
                Text = "Eye Elevation 原始值",
                ToolTip = "Revit 原始值，從內部原點量起"
            };
            _colEyeElevation = new DataGridTextColumn
            {
                Header = eyeElevationHeader,
                Binding = new WpfBinding(nameof(CameraRow.EyeElevationDisplay)) { Mode = System.Windows.Data.BindingMode.OneWay },
                Width = 110,
                IsReadOnly = true
            };
            _grid.Columns.Add(_colEyeElevation);

            ApplyColumnVisibilityFromSettings();
        }

        // 欄位鍵（Tag）：Height/HAngle/Pitch 走 NumericFieldEditCommitted；X/Y 走 PlaneFieldEditCommitted；FarClip 走 FarClipEditCommitted。
        private const string FieldHeight = "Height";
        private const string FieldHAngle = "HAngle";
        private const string FieldPitch = "Pitch";
        private const string FieldPlaneX = "X";
        private const string FieldPlaneY = "Y";
        private const string FieldFarClip = "FarClip";

        private static string FieldLabel(string key)
        {
            switch (key)
            {
                case FieldHeight: return "離樓板cm";
                case FieldHAngle: return "水平角°";
                case FieldPitch: return "俯仰角°";
                case FieldPlaneX: return "平面X cm";
                case FieldPlaneY: return "平面Y cm";
                case FieldFarClip: return "遠裁剪cm";
                default: return key;
            }
        }

        private static Style BuildEllipsisElementStyle()
        {
            Style s = new Style(typeof(TextBlock));
            s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            s.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new WpfBinding(nameof(CameraRow.Name))));
            return s;
        }

        private static Style BuildRightAlignedElementStyle()
        {
            Style s = new Style(typeof(TextBlock));
            s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            return s;
        }

        /// <summary>常駐 TextBox 儲存格：Text 單向繫結到列的顯示值；提交（Enter／失焦）在程式碼內處理，
        /// 絕不經過 DataGrid 的 BeginEdit/CommitEdit（Revit 停靠面板內那條路實測三輪都進不去）。</summary>
        private DataGridTemplateColumn BuildLiveNumericColumn(string header, string fieldKey, string bindingPath, string stringFormat, double width, string sortMemberPath)
        {
            FrameworkElementFactory tb = new FrameworkElementFactory(typeof(TextBox));
            tb.SetValue(FrameworkElement.StyleProperty, _cellTextBoxStyle);
            tb.SetValue(FrameworkElement.TagProperty, fieldKey);
            WpfBinding textBinding = new WpfBinding(bindingPath) { Mode = System.Windows.Data.BindingMode.OneWay };
            if (!string.IsNullOrEmpty(stringFormat)) textBinding.StringFormat = stringFormat;
            tb.SetBinding(TextBox.TextProperty, textBinding);
            if (fieldKey == FieldFarClip)
            {
                tb.SetBinding(TextBox.IsReadOnlyProperty, new WpfBinding(nameof(CameraRow.FarClipReadOnly)) { Mode = System.Windows.Data.BindingMode.OneWay });
                tb.SetValue(FrameworkElement.ToolTipProperty, "遠裁剪距離（cm）；灰字＝此相機查無可寫入的遠裁剪參數");
            }
            tb.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(CellTextBox_PreviewMouseLeftButtonDown));
            tb.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(CellTextBox_GotKeyboardFocus));
            tb.AddHandler(UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(CellTextBox_LostKeyboardFocus));
            tb.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(CellTextBox_PreviewKeyDown));
            tb.AddHandler(UIElement.PreviewTextInputEvent, new TextCompositionEventHandler(CellTextBox_PreviewTextInput));

            return new DataGridTemplateColumn
            {
                Header = header,
                Width = width,
                IsReadOnly = true, // DataGrid 本身不進編輯模式；編輯由儲存格內常駐 TextBox 自己處理
                SortMemberPath = sortMemberPath,
                CanUserSort = true,
                CellTemplate = new DataTemplate { VisualTree = tb }
            };
        }

        /// <summary>樓層欄：常駐 ComboBox（ItemsSource 繫結到本元件的 <see cref="LevelOptionsProperty"/>，
        /// SelectedValue 單向繫結到列的 LevelId）。下拉由使用者開啟後關閉時比對並提交。</summary>
        internal const string LevelCopyTooltip = "選另一個樓層＝在同位置複製一台相機到該樓層（原相機不變）";

        private DataGridTemplateColumn BuildLevelColumn()
        {
            FrameworkElementFactory cb = new FrameworkElementFactory(typeof(ComboBox));
            cb.SetValue(FrameworkElement.StyleProperty, _cellComboStyle);
            cb.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(LevelOption.Name));
            cb.SetValue(Selector.SelectedValuePathProperty, nameof(LevelOption.LevelId));
            cb.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding(nameof(LevelOptions)) { Source = this, Mode = System.Windows.Data.BindingMode.OneWay });
            cb.SetBinding(Selector.SelectedValueProperty, new WpfBinding(nameof(CameraRow.LevelId)) { Mode = System.Windows.Data.BindingMode.OneWay });
            // 2026-09-25：改樓層＝複製（使用者決策：相機樓層不就地修改）。
            cb.SetValue(FrameworkElement.ToolTipProperty, LevelCopyTooltip);
            // DropDownOpened/Closed 是 CLR 事件（非路由），在 Loaded（路由）時掛上；先解再掛避免列回收重複掛。
            cb.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(LevelCombo_Loaded));
            // 2026-09-25：下拉展開中選了新樓層就立即提交（與 DropDownClosed 先到者為準、只提交一次）。
            cb.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(LevelCombo_SelectionChanged));
            cb.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(LevelCombo_PreviewMouseWheel));
            cb.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(LevelCombo_PreviewKeyDown));

            return new DataGridTemplateColumn
            {
                Header = "樓層",
                Width = 64,
                MinWidth = 64,
                IsReadOnly = true,
                // LevelId（ElementId）沒有實作非泛型 IComparable → 用樓層名稱排序（2026-09-24 崩潰實證）。
                SortMemberPath = nameof(CameraRow.LevelName),
                CanUserSort = true,
                CellTemplate = new DataTemplate { VisualTree = cb }
            };
        }

        /// <summary>樓層清單（ComboBox ItemsSource 繫結目標）。用 DependencyProperty，換清單時所有已產生的
        /// 儲存格 ComboBox 自動更新，不必 Items.Refresh()。</summary>
        public static readonly DependencyProperty LevelOptionsProperty = DependencyProperty.Register(
            nameof(LevelOptions), typeof(List<LevelOption>), typeof(CameraListSection), new PropertyMetadata(null));

        public List<LevelOption> LevelOptions
        {
            get => (List<LevelOption>)GetValue(LevelOptionsProperty);
            set => SetValue(LevelOptionsProperty, value);
        }

        // ══════════════════════════════════════════════════════════
        // 選用欄位：可見性（右鍵標題選單）
        // ══════════════════════════════════════════════════════════

        private void ApplyColumnVisibilityFromSettings()
        {
            _colPlaneX.Visibility = Settings.ShowColumnPlaneX ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _colPlaneY.Visibility = Settings.ShowColumnPlaneY ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _colFarClip.Visibility = Settings.ShowColumnFarClip ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _colFov.Visibility = Settings.ShowColumnFov ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _colEyeElevation.Visibility = Settings.ShowColumnEyeElevation ? WpfVisibility.Visible : WpfVisibility.Collapsed;
        }

        /// <summary>2026-09-24 v5：原本只處理「右鍵點標題列」（選用欄位勾選選單）；現在多一支
        /// 「右鍵點資料列」分支，改由 <see cref="RowContextMenuRequested"/> 交回面板顯示
        /// 一鍵 4 視圖／僅打開透視圖／重新命名…／複製到其他樓層…／重設剖面位置的選單
        /// （面板才有這些動作要用到的方法，本類別刻意不碰 Revit API／不知道這些動作存在）。</summary>
        private void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { GridRightClick(e); }
            catch (Exception ex) { Logger.Error("相機清單：右鍵處理失敗", ex); }
        }

        private void GridRightClick(MouseButtonEventArgs e)
        {
            DependencyObject walk = e.OriginalSource as DependencyObject;
            while (walk != null && !(walk is DataGridColumnHeader) && !(walk is DataGridRow))
                walk = (walk is Visual || walk is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(walk) : LogicalTreeHelper.GetParent(walk);

            if (walk is DataGridColumnHeader header)
            {
                e.Handled = true;
                ShowColumnVisibilityMenu(header);
                return;
            }

            if (walk is DataGridRow dgRow && dgRow.Item is CameraRow row)
            {
                e.Handled = true;
                RowContextMenuRequested?.Invoke(row, dgRow);
            }
        }

        private void ShowColumnVisibilityMenu(UIElement placementTarget)
        {
            ContextMenu menu = new ContextMenu { FontFamily = CameraManagerTheme.UiFont, FontSize = CameraManagerTheme.BodyFontSize };
            AddColumnToggleItem(menu, "平面 X cm", () => Settings.ShowColumnPlaneX, v => Settings.ShowColumnPlaneX = v, _colPlaneX);
            AddColumnToggleItem(menu, "平面 Y cm", () => Settings.ShowColumnPlaneY, v => Settings.ShowColumnPlaneY = v, _colPlaneY);
            AddColumnToggleItem(menu, "遠裁剪 cm", () => Settings.ShowColumnFarClip, v => Settings.ShowColumnFarClip = v, _colFarClip);
            AddColumnToggleItem(menu, "視野角 °（估算）", () => Settings.ShowColumnFov, v => Settings.ShowColumnFov = v, _colFov);
            AddColumnToggleItem(menu, "Eye Elevation 原始值", () => Settings.ShowColumnEyeElevation, v => Settings.ShowColumnEyeElevation = v, _colEyeElevation);

            menu.PlacementTarget = placementTarget;
            menu.IsOpen = true;
        }

        /// <summary>2026-09-24 v5（2026-09-26 改為雙擊）：雙擊名稱儲存格＝觸發開啟動作（不進編輯模式——名稱欄已經改
        /// 唯讀）。用 PreviewMouseLeftButtonDown 直接判斷點在哪個 DataGridCell，比
        /// CurrentCellChanged（只在「目前儲存格真的變了」才觸發）更精確地對應「使用者點了
        /// 一下」這個手勢——同一格連點兩次也都要能各自觸發一次開啟動作。不 e.Handled，讓
        /// DataGrid 照常完成選取／取得焦點。</summary>
        private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DependencyObject walk = e.OriginalSource as DependencyObject;
                while (walk != null && !(walk is DataGridCell) && !(walk is DataGridRow))
                    walk = (walk is Visual || walk is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(walk) : LogicalTreeHelper.GetParent(walk);

                if (!(walk is DataGridCell cell)) return;
                if (!(cell.DataContext is CameraRow row)) return;

                // 數字／樓層欄：儲存格內是常駐 TextBox／ComboBox，自己處理點擊（見 CellTextBox_PreviewMouseLeftButtonDown）。
                // 2026-09-26 第四輪（使用者決策）：單擊名稱＝只選取（DataGrid 照常選取）；雙擊名稱＝開啟動作（一鍵 4 視圖）。
                if (cell.Column == _nameColumn && e.ClickCount >= 2)
                {
                    Logger.Info($"相機清單：雙擊名稱「{row.Name}」→ 開啟動作。");
                    RowOpenActionRequested?.Invoke(row);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("相機清單：點擊處理失敗", ex);
            }
        }

        private string _lastSelectionKey = "";

        /// <summary>只轉發 DataGrid 本身、且選取集合真的改變時的選取變化。DataGrid.SelectionChanged
        /// 是冒泡路由事件：「樓層」欄每一列的顯示用 ComboBox（DataGridComboBoxColumn 的
        /// TextBlockComboBox）在 Items.Refresh()／換 ItemsSource 時都會各自 raise SelectionChanged
        /// 冒泡上來——以前沒過濾，被當成「使用者點選」，每台相機各排一次 RefreshSelectedSectionLine
        /// （2026-09-24 RevitMCP log：每次「底圖匯出結束」→RefreshStatusColumn 後同一秒 10–15 筆）。</summary>
        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, _grid)) return;
            List<CameraRow> selected = GetSelectedRows();
            string key = string.Join(",", selected.Select(r => r.ViewId != null ? r.ViewId.GetIdValue().ToString() : "?"));
            if (key == _lastSelectionKey) return;
            _lastSelectionKey = key;
            SelectionChanged?.Invoke(selected, _programmaticSelection == 0);
        }

        private static void AddColumnToggleItem(ContextMenu menu, string text, Func<bool> getter, Action<bool> setter, DataGridColumn column)
        {
            MenuItem item = new MenuItem { Header = text, IsCheckable = true, IsChecked = getter() };
            item.Click += (s, e) =>
            {
                bool newValue = item.IsChecked;
                setter(newValue);
                column.Visibility = newValue ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                ConfigManager.Instance.SaveSettings();
            };
            menu.Items.Add(item);
        }

        // ══════════════════════════════════════════════════════════
        // 資料繫結
        // ══════════════════════════════════════════════════════════

        // ── 編輯中不重建列 ──
        private bool _pendingRows;
        private bool _pendingLevels;
        private bool _pendingStatusRefresh;
        private bool _afterEditScheduled;

        /// <summary>2026-09-25：任何儲存格 TextBox 擁有鍵盤焦點、或樓層下拉展開中。這段期間換 ItemsSource／
        /// Items.Refresh() 會重建儲存格、把使用者打到一半的字丟掉，一律延後到焦點離開。</summary>
        public bool IsEditing
        {
            get
            {
                try
                {
                    if (_levelDropDownOpen) return true;
                    TextBox tb = _activeCellTextBox;
                    return tb != null && tb.IsKeyboardFocusWithin;
                }
                catch { return false; }
            }
        }

        private void ScheduleAfterEdit()
        {
            if (_afterEditScheduled) return;
            _afterEditScheduled = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _afterEditScheduled = false;
                try
                {
                    ApplyPendingUpdates();
                }
                catch (Exception ex)
                {
                    Logger.Error("相機清單：編輯結束後套用延後更新失敗", ex);
                }
            }));
        }

        private void ApplyPendingUpdates()
        {
            if (IsEditing) return;
            if (_pendingLevels) { _pendingLevels = false; ApplyLevels(); }
            if (_pendingRows)
            {
                _pendingRows = false;
                _pendingStatusRefresh = false; // 換 ItemsSource 本身就會重跑 LoadingRow
                Logger.Info("相機清單：編輯結束，套用延後的清單更新。");
                _programmaticSelection++;
                try { ApplyFilter(); }
                finally { _programmaticSelection--; }
            }
            if (_pendingStatusRefresh) { _pendingStatusRefresh = false; _grid.Items.Refresh(); }
        }

        public void SetLevels(List<LevelOption> levels)
        {
            _levels = levels ?? new List<LevelOption>();
            if (IsEditing) { _pendingLevels = true; return; }
            ApplyLevels();
        }

        private void ApplyLevels()
        {
            LevelOptions = _levels; // 儲存格 ComboBox 的 ItemsSource 繫結到這個 DP

            // 「樓層」欄寬度依實際模型內最長樓層名稱動態調整（最小 64px），修掉窄面板下
            // 「1F」被裁成「F」的問題（2026-09-24 實測回報）。
            double widest = 64;
            if (_levels.Count > 0)
            {
                Typeface typeface = new Typeface(CameraManagerTheme.UiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                foreach (LevelOption lv in _levels)
                {
                    string text = lv.Name ?? "";
#pragma warning disable CS0618 // FormattedText 建構子在新版 WPF 標示過時但仍可用，這裡只是量字寬，不涉及排版渲染
                    WpfFormattedText ft = new WpfFormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        typeface, CameraManagerTheme.BodyFontSize, System.Windows.Media.Brushes.Black, dpi);
#pragma warning restore CS0618
                    widest = Math.Max(widest, ft.WidthIncludingTrailingWhitespace + 18); // +18：左右內距與些許餘量
                }
            }
            _levelColumn.Width = new DataGridLength(Math.Min(widest, 140));
        }

        public void SetRows(List<CameraRow> rows)
        {
            _allRows = rows ?? new List<CameraRow>();
            if (IsEditing)
            {
                _pendingRows = true;
                Logger.Info("相機清單：儲存格編輯中，延後換資料（編輯結束後套用）。");
                return;
            }
            _programmaticSelection++;
            try { ApplyFilter(); }
            finally { _programmaticSelection--; }
        }

        public List<CameraRow> GetSelectedRows() => _grid.SelectedItems.Cast<CameraRow>().ToList();

        /// <summary>程式化選取（預設選取、開啟動作前確保選取）：觸發的 SelectionChanged 一律
        /// userInitiated=false。</summary>
        public void SelectByViewId(ElementId viewId)
        {
            if (IsEditing) return; // 編輯中不動選取（編輯中的格子本來就在使用者選的那一列）
            CameraRow match = (_grid.ItemsSource as IEnumerable<CameraRow>)?.FirstOrDefault(r => r.ViewId == viewId);
            if (match == null) return;
            if (ReferenceEquals(_grid.SelectedItem, match) && _grid.SelectedItems.Count == 1) return;
            _programmaticSelection++;
            try
            {
                _grid.SelectedItem = match;
            }
            finally
            {
                _programmaticSelection--;
            }
            // ScrollIntoView 延後到版面穩定後（Background 優先權），不在資料剛換掉、DataGrid 尚未
            // 量測時硬捲動。
            try
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    try { if (_grid.Items.Contains(match)) _grid.ScrollIntoView(match); } catch { }
                }));
            }
            catch { }
        }

        /// <summary>
        /// 2026-09-26 第六輪（編輯快速路徑）：只更新這一列畫面（呼叫端已改好 CameraRow 的值）——不換 ItemsSource、
        /// 不 Items.Refresh()。逐一 UpdateTarget 這一列儲存格內的 TextBox／TextBlock 繫結、重套列樣式（橘色＝高度警示）。
        /// 使用者目前正在輸入的那一格（鍵盤焦點）不動，免得把打到一半的字蓋掉。row＝null 時以 Id 找，
        /// 用於失敗時把文字還原成原值。編輯中也照做（這是單列、不重建儲存格）。
        /// </summary>
        public void UpdateRowInPlace(ElementId viewId, CameraRow row)
        {
            try
            {
                CameraRow target = row ?? _allRows?.FirstOrDefault(r => r.ViewId == viewId);
                if (target == null) return;
                if (!(_grid.ItemContainerGenerator.ContainerFromItem(target) is DataGridRow dataRow)) return; // 不在畫面上（虛擬化）：下次產生時自然讀新值
                ApplyRowStyle(dataRow, target);
                int n = 0;
                var stack = new Stack<DependencyObject>();
                stack.Push(dataRow);
                while (stack.Count > 0)
                {
                    DependencyObject d = stack.Pop();
                    if (d is TextBox tb)
                    {
                        if (!ReferenceEquals(tb, _activeCellTextBox) || !tb.IsKeyboardFocusWithin)
                        {
                            System.Windows.Data.BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateTarget();
                            _committedText.Remove(tb);
                            n++;
                        }
                        continue;
                    }
                    if (d is TextBlock tbk)
                    {
                        System.Windows.Data.BindingOperations.GetBindingExpression(tbk, TextBlock.TextProperty)?.UpdateTarget();
                        n++;
                    }
                    int count = VisualTreeHelper.GetChildrenCount(d);
                    for (int i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(d, i));
                }
                Logger.Info($"相機清單：就地更新「{RowLabel(target)}」一列（{n} 個儲存格繫結，不重建清單）。");
            }
            catch (Exception ex)
            {
                Logger.Error("相機清單：就地更新單列失敗", ex);
            }
        }

        public void RefreshStatusColumn()
        {
            if (IsEditing) { _pendingStatusRefresh = true; return; }
            _grid.Items.Refresh();
        }

        /// <summary>由常駐標題列的搜尋框呼叫（規格：搜尋框在 Header，不在這個分頁裡）。</summary>
        public void SetSearchFilter(string keyword)
        {
            _searchKeyword = keyword?.Trim() ?? "";
            if (IsEditing) { _pendingRows = true; return; }
            _programmaticSelection++;
            try { ApplyFilter(); }
            finally { _programmaticSelection--; }
        }

        private void ApplyFilter()
        {
            IEnumerable<CameraRow> filtered = string.IsNullOrEmpty(_searchKeyword)
                ? _allRows
                : _allRows.Where(r => r.Name != null && r.Name.IndexOf(_searchKeyword, StringComparison.OrdinalIgnoreCase) >= 0);
            _grid.ItemsSource = filtered.ToList();
        }

        // ══════════════════════════════════════════════════════════
        // 編輯（2026-09-25 第三次修正）：常駐 TextBox／ComboBox，不走 DataGrid 編輯模式
        // ══════════════════════════════════════════════════════════
        //
        // 前兩輪（單擊 BeginEdit、CellEditEnding 不設 Cancel）實機都失敗：RevitMCP log 在測試期間完全沒有
        // 任何編輯相關紀錄＝DataGrid 根本沒進編輯模式。DataGrid 的「進編輯 ↔ 鍵盤焦點」握手在 Revit 停靠面板
        // 內不可靠（Revit 主視窗會搶焦點；另外 live sync 每隔幾秒 SetRows 換 ItemsSource 也會把格子踢出編輯）。
        // 改法：DataGrid 全唯讀，儲存格內直接放一個一直可輸入的 TextBox，提交在這裡自己處理：
        //   點一下 → 取得鍵盤焦點＋全選（同時選取該列，地圖高亮）；打字；Enter 或離開 → 解析、比對、送事件；
        //   Esc → 還原原值。焦點在任何儲存格 TextBox 內時，清單重建一律延後到焦點離開（IsEditing）。

        /// <summary>目前擁有鍵盤焦點的儲存格 TextBox（null＝沒有）。</summary>
        private TextBox _activeCellTextBox;
        /// <summary>樓層下拉目前展開中（使用者開的）。</summary>
        private bool _levelDropDownOpen;
        /// <summary>這一段焦點期間是否已記錄過「收到鍵盤輸入」（診斷用，避免每個字元一行 log）。</summary>
        private bool _loggedTextInputThisFocus;
        private bool _loggedKeyDownThisFocus;
        /// <summary>Enter 已提交的文字：之後失焦時同一段文字不再提交第二次（Revit 重讀前列值還是舊的）。</summary>
        private readonly Dictionary<TextBox, string> _committedText = new Dictionary<TextBox, string>();

        private static string RowLabel(CameraRow row) => row?.Name ?? "?";

        private void CellTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (!(sender is TextBox tb)) return;
                if (tb.IsKeyboardFocusWithin) return; // 已在輸入中：交給 TextBox 自己移動游標／選字
                // 第一次點：自己拿焦點＋全選，e.Handled 讓 TextBox 不把游標放到點擊處（否則全選會被取消），
                // 也讓 DataGridCell 不再做它自己的焦點/選取處理。
                e.Handled = true;
                FocusCellTextBox(tb);
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：點擊儲存格取得焦點失敗", ex);
            }
        }

        private void FocusCellTextBox(TextBox tb)
        {
            tb.Focus();
            IInputElement got = Keyboard.Focus(tb);
            if (!ReferenceEquals(got, tb) && !tb.IsKeyboardFocusWithin)
            {
                Logger.Info($"清單編輯：{RowLabel(tb.DataContext as CameraRow)} {FieldLabel(tb.Tag as string)} 無法取得鍵盤焦點" +
                            $"（Keyboard.Focus 回傳 {got?.GetType().Name ?? "null"}；可能被 Revit 主視窗攔走）");
            }
            tb.SelectAll();
        }

        private void CellTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (!(sender is TextBox tb) || !ReferenceEquals(e.NewFocus, tb)) return;
                _activeCellTextBox = tb;
                _loggedTextInputThisFocus = false;
                _loggedKeyDownThisFocus = false;
                _committedText.Remove(tb);
                tb.SelectAll();

                CameraRow row = tb.DataContext as CameraRow;
                Logger.Info($"清單編輯：{RowLabel(row)} {FieldLabel(tb.Tag as string)} 取得鍵盤焦點");
                // 選取該列（使用者操作 → SelectionChanged userInitiated=true → 地圖高亮）。
                // 絕不觸發一鍵 4 視圖（那只由名稱欄雙擊觸發）。
                if (row != null && (!ReferenceEquals(_grid.SelectedItem, row) || _grid.SelectedItems.Count != 1))
                    _grid.SelectedItem = row;
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：取得焦點處理失敗", ex);
            }
        }

        private void CellTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (!(sender is TextBox tb) || !ReferenceEquals(e.OldFocus, tb)) return;
                bool alreadyCommitted = _committedText.TryGetValue(tb, out string committed) && committed == tb.Text;
                _committedText.Remove(tb);
                if (!alreadyCommitted) CommitCellTextBox(tb, "離開欄位");
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：失焦提交失敗", ex);
            }
            finally
            {
                if (ReferenceEquals(_activeCellTextBox, sender)) _activeCellTextBox = null;
                ScheduleAfterEdit();
            }
        }

        private void CellTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (!(sender is TextBox tb)) return;
                if (!_loggedKeyDownThisFocus)
                {
                    _loggedKeyDownThisFocus = true;
                    Logger.Info($"清單編輯：{RowLabel(tb.DataContext as CameraRow)} {FieldLabel(tb.Tag as string)} 收到按鍵 {e.Key}");
                }
                if (e.Key == Key.Enter)
                {
                    e.Handled = true; // 不讓 DataGrid 把 Enter 當成「往下一列」
                    if (CommitCellTextBox(tb, "Enter")) _committedText[tb] = tb.Text;
                    MoveFocusOffCell();
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    RestoreCellTextBox(tb);
                    _committedText[tb] = tb.Text; // 還原後的文字＝原值，失焦時不再提交
                    Logger.Info($"清單編輯：{RowLabel(tb.DataContext as CameraRow)} {FieldLabel(tb.Tag as string)} Esc 取消，還原原值");
                    MoveFocusOffCell();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：Enter/Esc 處理失敗", ex);
            }
        }

        private void CellTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (_loggedTextInputThisFocus || !(sender is TextBox tb)) return;
                _loggedTextInputThisFocus = true;
                Logger.Info($"清單編輯：{RowLabel(tb.DataContext as CameraRow)} {FieldLabel(tb.Tag as string)} 收到文字輸入（鍵盤輸入有送達 TextBox）");
            }
            catch { }
        }

        /// <summary>Enter/Esc 之後把焦點交給 DataGrid 本身，讓延後的清單更新可以套用（焦點還在 TextBox 內時
        /// 一律不重建清單）。</summary>
        private void MoveFocusOffCell()
        {
            try
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    try { _grid.Focus(); } catch (Exception ex) { Logger.Error("清單編輯：移開焦點失敗", ex); }
                }));
            }
            catch (Exception ex) { Logger.Error("清單編輯：排程移開焦點失敗", ex); }
        }

        private static void RestoreCellTextBox(TextBox tb)
        {
            try
            {
                System.Windows.Data.BindingExpression be = System.Windows.Data.BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
                be?.UpdateTarget();
                tb.SelectAll();
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：還原原值失敗", ex);
            }
        }

        private static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().Replace('，', ',').Replace('．', '.').Replace('－', '-').Replace("°", "").Trim();
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>解析、比對（±0.05）、記錄、送事件。回傳 true＝已送出提交事件。</summary>
        private bool CommitCellTextBox(TextBox tb, string trigger)
        {
            CameraRow row = tb.DataContext as CameraRow;
            string key = tb.Tag as string;
            if (row == null || string.IsNullOrEmpty(key)) return false;
            if (tb.IsReadOnly) return false;

            double? current;
            switch (key)
            {
                case FieldHeight: current = row.HeightAboveLevelCm; break;
                case FieldHAngle: current = row.HorizontalAngleDeg; break;
                case FieldPitch: current = row.PitchDeg; break;
                case FieldPlaneX: current = row.EyeXCm; break;
                case FieldPlaneY: current = row.EyeYCm; break;
                case FieldFarClip: current = row.FarClipCm; break;
                default: return false;
            }
            string oldText = current.HasValue ? current.Value.ToString("F1", CultureInfo.InvariantCulture) : "—";
            string text = tb.Text ?? "";

            if (!TryParseNumber(text, out double value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                Logger.Info($"清單編輯：{RowLabel(row)} {FieldLabel(key)} 輸入「{text}」無法解析為數字（{trigger}），還原 {oldText}");
                RestoreCellTextBox(tb);
                return false;
            }
            if (current.HasValue && Math.Abs(current.Value - value) < 0.05)
            {
                RestoreCellTextBox(tb); // 值沒變：不開交易；把文字格式化回原樣
                return false;
            }
            if (key == FieldFarClip && !row.FarClipEditable)
            {
                Logger.Info($"清單編輯：{RowLabel(row)} {FieldLabel(key)} 此相機遠裁剪不可寫入，忽略輸入 {value:F1}");
                RestoreCellTextBox(tb);
                return false;
            }

            Logger.Info($"清單編輯：{RowLabel(row)} {FieldLabel(key)} {oldText}→{value.ToString("F1", CultureInfo.InvariantCulture)}（{trigger}）");
            switch (key)
            {
                case FieldHeight:
                case FieldHAngle:
                case FieldPitch:
                    NumericFieldEditCommitted?.Invoke(row, key, value);
                    break;
                case FieldPlaneX:
                case FieldPlaneY:
                    PlaneFieldEditCommitted?.Invoke(row, key, value);
                    break;
                case FieldFarClip:
                    FarClipEditCommitted?.Invoke(row, value);
                    break;
            }
            return true;
        }

        // ── 樓層 ComboBox ──

        private void LevelCombo_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is ComboBox combo)) return;
                combo.DropDownOpened -= LevelCombo_DropDownOpened;
                combo.DropDownOpened += LevelCombo_DropDownOpened;
                combo.DropDownClosed -= LevelCombo_DropDownClosed;
                combo.DropDownClosed += LevelCombo_DropDownClosed;
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：樓層下拉掛事件失敗", ex);
            }
        }

        // 2026-09-25 第六輪（log：「樓層 下拉展開」之後什麼都沒有＝從未提交）：展開時記住「哪個 combo／哪一列／
        // 原本樓層 Id 的數值」，之後 SelectionChanged（展開中）或 DropDownClosed 先到者提交一次；比較一律用
        // ElementId 數值，不比參照；每條沒提交的路徑都寫 log，方便實機判讀。
        private ComboBox _levelOpenCombo;
        private CameraRow _levelOpenRow;
        private long? _levelOpenOriginalId;
        private bool _levelCommitDone;

        private void LevelCombo_DropDownOpened(object sender, EventArgs e)
        {
            try
            {
                _levelDropDownOpen = true;
                _levelCommitDone = false;
                _levelOpenCombo = sender as ComboBox;
                _levelOpenRow = _levelOpenCombo?.DataContext as CameraRow;
                _levelOpenOriginalId = _levelOpenRow?.LevelId != null ? (long?)_levelOpenRow.LevelId.GetIdValue() : null;
                if (_levelOpenRow != null)
                {
                    CameraRow row = _levelOpenRow;
                    Logger.Info($"清單編輯：{RowLabel(row)} 樓層 下拉展開（目前 {row.LevelName ?? "—"} id={_levelOpenOriginalId?.ToString() ?? "null"}）");
                    if (!ReferenceEquals(_grid.SelectedItem, row) || _grid.SelectedItems.Count != 1)
                        _grid.SelectedItem = row;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：樓層下拉展開處理失敗", ex);
            }
        }

        /// <summary>下拉展開中選取變化＝使用者點了（或用方向鍵移到）某個樓層 → 立即提交（只一次）。</summary>
        private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (!(sender is ComboBox combo)) return;
                if (!_levelDropDownOpen || !combo.IsDropDownOpen || !ReferenceEquals(combo, _levelOpenCombo)) return;
                TryCommitLevel(combo, "展開中選取");
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：樓層選取變化處理失敗", ex);
            }
        }

        /// <summary>使用者開的下拉關閉時比對選取值，與列的 LevelId 不同就提交（若展開中尚未提交）。</summary>
        private void LevelCombo_DropDownClosed(object sender, EventArgs e)
        {
            try
            {
                if (!_levelDropDownOpen)
                {
                    Logger.Info("清單編輯：樓層 下拉關閉，但展開旗標已清除（略過）");
                    return;
                }
                _levelDropDownOpen = false;
                if (!(sender is ComboBox combo)) return;
                if (_levelCommitDone) return; // 展開中已提交過
                TryCommitLevel(combo, "下拉關閉");
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：樓層提交失敗", ex);
            }
            finally
            {
                _levelDropDownOpen = false;
                _levelOpenCombo = null;
                ScheduleAfterEdit();
            }
        }

        private void TryCommitLevel(ComboBox combo, string trigger)
        {
            if (_levelCommitDone) return;
            CameraRow row = _levelOpenRow ?? combo.DataContext as CameraRow;
            if (row == null) { Logger.Info($"清單編輯：樓層 {trigger}，找不到列（略過）"); return; }
            ElementId newLevelId = combo.SelectedValue as ElementId ?? (combo.SelectedItem as LevelOption)?.LevelId;
            if (newLevelId == null)
            {
                if (trigger == "下拉關閉")
                    Logger.Info($"清單編輯：{RowLabel(row)} 樓層 {trigger}，沒有選取值（SelectedValue={combo.SelectedValue?.GetType().Name ?? "null"}，略過）");
                return;
            }
            long newVal = newLevelId.GetIdValue();
            long? oldVal = _levelOpenOriginalId ?? (row.LevelId != null ? (long?)row.LevelId.GetIdValue() : null);
            if (oldVal.HasValue && newVal == oldVal.Value)
            {
                if (trigger == "下拉關閉") Logger.Info($"清單編輯：{RowLabel(row)} 樓層 {trigger}，未變更（id={newVal}）");
                return;
            }

            _levelCommitDone = true;
            string newName = (combo.SelectedItem as LevelOption)?.Name ?? newVal.ToString();
            Logger.Info($"清單編輯：{RowLabel(row)} 樓層 {row.LevelName ?? "—"}→{newName}（{trigger}，改樓層＝複製相機）");
            LevelEditCommitted?.Invoke(row, newLevelId);
            // 2026-09-25 使用者決策：改樓層＝複製，原相機不變 → 原列的樓層格還原顯示原樓層。
            ScheduleRevertLevelCombo(combo, row);
        }

        /// <summary>把樓層 ComboBox 的顯示還原成列的原樓層（延到 Background，讓下拉先關閉）。
        /// 重新讀取 OneWay 繫結；繫結不在時退回 SetCurrentValue。還原引起的 SelectionChanged 因下拉已關閉而被忽略。</summary>
        private void ScheduleRevertLevelCombo(ComboBox combo, CameraRow row)
        {
            if (combo == null || row == null) return;
            try
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        if (!ReferenceEquals(combo.DataContext, row)) return; // 列已回收／清單已刷新
                        if (combo.IsDropDownOpen) combo.IsDropDownOpen = false; // DropDownClosed 看到已提交 → 不再提交
                        var be = System.Windows.Data.BindingOperations.GetBindingExpression(combo, Selector.SelectedValueProperty);
                        if (be != null) be.UpdateTarget();
                        else combo.SetCurrentValue(Selector.SelectedValueProperty, row.LevelId);
                        Logger.Info($"清單編輯：{RowLabel(row)} 樓層格還原顯示 {row.LevelName ?? "—"}（原相機不變）");
                    }
                    catch (Exception ex) { Logger.Error("清單編輯：樓層格還原顯示失敗", ex); }
                }));
            }
            catch (Exception ex) { Logger.Error("清單編輯：排程樓層格還原失敗", ex); }
        }

        /// <summary>下拉沒展開時，滾輪不改樓層（WPF ComboBox 取得焦點後滾輪會直接換選項，而那不會提交、只會讓
        /// 顯示值與模型不一致）——改把滾輪轉給 DataGrid 捲動。</summary>
        private void LevelCombo_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                if (!(sender is ComboBox combo) || combo.IsDropDownOpen) return;
                e.Handled = true;
                var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = combo };
                _grid.RaiseEvent(forwarded);
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：樓層滾輪轉送失敗", ex);
            }
        }

        /// <summary>下拉沒展開時，上下鍵不直接換樓層（同上理由）；Alt+↓／F4 仍可開下拉。</summary>
        private void LevelCombo_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (!(sender is ComboBox combo) || combo.IsDropDownOpen) return;
                if ((e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.PageUp || e.Key == Key.PageDown
                     || e.Key == Key.Home || e.Key == Key.End) && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
                    e.Handled = true;
            }
            catch (Exception ex)
            {
                Logger.Error("清單編輯：樓層按鍵處理失敗", ex);
            }
        }

        // ── 2026-09-26 第四輪：目前正在看的透視相機（作用中視圖）在清單上的標示：粗體＋左側 accent 條＋淡 accent 底 ──
        private ElementId _activeCameraId;

        /// <summary>設定目前正在看的相機（null＝作用中視圖不是透視相機）。只重畫受影響的兩列，不整張清單重建。</summary>
        public void SetActiveCameraId(ElementId viewId)
        {
            try
            {
                ElementId old = _activeCameraId;
                if ((old == null && viewId == null) || (old != null && viewId != null && old.GetIdValue() == viewId.GetIdValue())) return;
                _activeCameraId = viewId;
                foreach (ElementId id in new[] { old, viewId })
                {
                    if (id == null) continue;
                    CameraRow r = (_grid.ItemsSource as IEnumerable<CameraRow>)?.FirstOrDefault(x => x.ViewId != null && x.ViewId.GetIdValue() == id.GetIdValue());
                    if (r == null) continue;
                    if (_grid.ItemContainerGenerator.ContainerFromItem(r) is DataGridRow dr) ApplyRowStyle(dr, r);
                }
            }
            catch (Exception ex) { Logger.Error("相機清單：標示目前檢視中的相機失敗", ex); }
        }

        private bool IsActiveCamera(CameraRow row) =>
            _activeCameraId != null && row?.ViewId != null && row.ViewId.GetIdValue() == _activeCameraId.GetIdValue();

        private void Grid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (!(e.Row.Item is CameraRow row)) return;
            ApplyRowStyle(e.Row, row);
        }

        private void ApplyRowStyle(DataGridRow dataRow, CameraRow row)
        {
            bool active = IsActiveCamera(row);
            dataRow.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            if (active)
            {
                dataRow.BorderBrush = CameraManagerTheme.Accent;
                dataRow.BorderThickness = new Thickness(4, 0, 0, 0);
            }
            else
            {
                dataRow.ClearValue(DataGridRow.BorderBrushProperty);
                dataRow.ClearValue(DataGridRow.BorderThicknessProperty);
            }
            string activeNote = active ? "▶ 目前正在看的透視相機" + Environment.NewLine : "";
            string storeyNote = string.IsNullOrEmpty(row.StoreyLog) ? "" : Environment.NewLine + "樓層判定：" + row.StoreyLog;

            // 2026-09-25：室內／室外判定放在列 ToolTip 前綴（300px 寬已無空間再加一欄，避免橫向捲動）。
            string io = row.IsOutdoor.HasValue
                ? (row.IsOutdoor.Value
                    ? $"室外（眼睛不在任何房間；Key Plan 扇形半徑 {RevitMCP.Core.CameraMath.FeetToMm(row.OutdoorFanRadiusFeet) / 1000.0:F1} m）"
                    : "室內（眼睛在房間內；Key Plan 以房間為主）")
                : "室內/室外：無法判定";

            if (row.HeightWarning == "偏低" || row.HeightWarning == "偏高")
            {
                dataRow.Background = new System.Windows.Media.SolidColorBrush(WpfColor.FromArgb(60, CameraManagerTheme.WarningColor.R, CameraManagerTheme.WarningColor.G, CameraManagerTheme.WarningColor.B));
                dataRow.ToolTip = activeNote + io + Environment.NewLine + "橘色列：離樓板高度不在 " +
                    $"{CameraManagerService.LowHeightWarningCm:F0}–{CameraManagerService.HighHeightWarningCm:F0} cm（" + (row.HeightWarning == "偏低"
                    ? $"低於 {CameraManagerService.LowHeightWarningCm:F0} cm，可能偏低）"
                    : $"高於 {CameraManagerService.HighHeightWarningCm:F0} cm，可能偏高）") + storeyNote;
            }
            else
            {
                // 目前檢視中的相機不另上底色（本機底色會蓋掉樣式的「選取中」底色），只用粗體＋左側 accent 條區分。
                dataRow.ClearValue(DataGridRow.BackgroundProperty);
                dataRow.ToolTip = activeNote + (row.InMapRange ? io : io + Environment.NewLine + "此相機不在目前底圖的裁剪範圍內") + storeyNote;
            }
        }
    }
}
