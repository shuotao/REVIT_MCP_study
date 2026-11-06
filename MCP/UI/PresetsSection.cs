using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Autodesk.Revit.DB;
using RevitMCP.Core;

using WpfGrid = System.Windows.Controls.Grid;

namespace RevitMCP.UI
{
    /// <summary>
    /// 4.5 人視高預設值套用、4.6 複製相機到其他樓層。
    ///
    /// 2026-09-24（面板拿掉分頁後的第二次重排）：
    ///   - <see cref="HeightPresetBar"/>：preset 列（[160] cm ＋ 套用到選取／套用到全部），
    ///     不變。
    ///   - 複製到其他樓層原本是「工具」分頁的一塊常駐面板，分頁拿掉後改成動作列上的一顆
    ///     按鈕＋<see cref="Popup"/> 彈出小卡（樓層勾選清單＋確定／取消），透過
    ///     Popup；2026-09-24 第四輪起由「④ ⋯ 更多」選單叫出（見 CopyPopup／OpenCopyToLevelsPopup）。
    ///   - 匯出 Excel 移到標題列圖示按鈕（見 <see cref="CameraManagerPanelControl"/>），
    ///     這裡不再持有匯出按鈕／事件。
    /// 這個類別本身不是 UserControl，純粹是這幾塊 UI 的組裝器＋事件來源。
    /// </summary>
    internal class PresetsSection
    {
        private readonly TextBox _heightBox;
        private readonly ListBox _levelCheckList;
        private readonly Popup _copyPopup;

        public FrameworkElement HeightPresetBar { get; }
        /// <summary>2026-09-24 第四輪：⑤ 人視高預設值不再佔一列，改成「④ ⋯ 更多 → ⑤ 人視高預設值…」
        /// 叫出的主題化小 Popup；呼叫端（面板）負責把兩個 Popup 掛進視覺樹（見 CameraManagerPanelControl）。</summary>
        public Popup HeightPopup { get; }
        public Popup CopyPopup => _copyPopup;

        /// <summary>「套用到選取」／「套用到全部」共用；bool 參數 true=全部、false=僅選取。</summary>
        public event Action<double, bool> ApplyHeightRequested;
        /// <summary>Popup 內按下「確定」，帶出勾選的樓層 Id 清單。</summary>
        public event Action<List<ElementId>> CopyToLevelsRequested;

        public double DefaultHeightCm
        {
            get => TryParseHeight(_heightBox.Text, out double v) ? v : 160.0;
            set => _heightBox.Text = value.ToString("F0", CultureInfo.InvariantCulture);
        }

        /// <summary>2026-09-26 稽核：與清單數字格相同——先 InvariantCulture、再目前地區設定（小數逗號地區輸入「162,5」不再被當成無效而默默用 160）。</summary>
        private static bool TryParseHeight(string text, out double value)
        {
            string t = (text ?? "").Trim();
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        public PresetsSection()
        {
            // ── 人視高預設值列 ──
            StackPanel heightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock heightLabel = CameraManagerTheme.FieldLabel("人視高預設值");
            heightLabel.Margin = new Thickness(0, 0, 6, 0);
            heightPanel.Children.Add(heightLabel);

            _heightBox = new TextBox { Width = 48, Text = "160", Margin = new Thickness(0, 0, 4, 0), ToolTip = "⑤ 人視高預設值：離樓板高度 (cm)" };
            CameraManagerTheme.StyleTextBox(_heightBox);
            heightPanel.Children.Add(_heightBox);

            TextBlock unit = CameraManagerTheme.Caption("cm");
            unit.VerticalAlignment = VerticalAlignment.Center;
            unit.Margin = new Thickness(0, 0, 8, 0);
            heightPanel.Children.Add(unit);

            Button applySelectedButton = CameraManagerTheme.SecondaryButton("套用到選取");
            applySelectedButton.Margin = new Thickness(0, 0, 6, 0);
            applySelectedButton.ToolTip = "⑤ 套用到選取：把選取相機的離樓板高度設為上方數值";
            applySelectedButton.Click += (s, e) =>
            {
                try
                {
                    if (TryParseHeight(_heightBox.Text, out double h) && h > 0)
                    {
                        HeightPopup.IsOpen = false;
                        ApplyHeightRequested?.Invoke(h, false);
                    }
                    else
                        MessageBox.Show("高度必須是正數 (cm)。", "套用人視高", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex) { Logger.Error("相機管理面板：套用到選取失敗", ex); }
            };
            heightPanel.Children.Add(applySelectedButton);

            Button applyAllButton = CameraManagerTheme.SecondaryButton("套用到全部");
            applyAllButton.ToolTip = "⑤ 套用到全部：把文件內所有相機的離樓板高度設為上方數值（會先確認）";
            applyAllButton.Click += (s, e) =>
            {
                try
                {
                    if (!TryParseHeight(_heightBox.Text, out double h) || h <= 0)
                    {
                        MessageBox.Show("高度必須是正數 (cm)。", "套用人視高", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    HeightPopup.IsOpen = false; // 先關 Popup，再跳確認對話框
                    var confirm = MessageBox.Show($"確定要把目前文件內所有相機的離樓板高度都設為 {h:F0} cm 嗎？",
                        "套用到全部", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm == MessageBoxResult.Yes) ApplyHeightRequested?.Invoke(h, true);
                }
                catch (Exception ex) { Logger.Error("相機管理面板：套用到全部失敗", ex); }
            };
            heightPanel.Children.Add(applyAllButton);

            HeightPresetBar = heightPanel;

            // ── ⑤ 人視高預設值 Popup（卡片：標題 + 上面那一列） ──
            Border heightCard = new Border
            {
                Background = CameraManagerTheme.Surface,
                BorderBrush = CameraManagerTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(CameraManagerTheme.CornerRadiusPx),
                Padding = new Thickness(CameraManagerTheme.Spacing)
            };
            StackPanel heightStack = new StackPanel();
            heightStack.Children.Add(CameraManagerTheme.SectionTitle("⑤ 人視高預設值"));
            heightStack.Children.Add(heightPanel);
            heightCard.Child = heightStack;
            HeightPopup = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, Child = heightCard };

            // ── 複製到其他樓層：Popup（樓層勾選清單 + 確定/取消），由選單項目叫出 ──
            _levelCheckList = new ListBox
            {
                SelectionMode = SelectionMode.Multiple, Height = 140, Width = 200,
                Margin = new Thickness(0, 0, 0, CameraManagerTheme.Spacing),
                Background = CameraManagerTheme.Surface, BorderBrush = CameraManagerTheme.BorderBrush, BorderThickness = new Thickness(1),
                Foreground = CameraManagerTheme.Text, FontFamily = CameraManagerTheme.UiFont
            };

            Border popupCard = new Border
            {
                Background = CameraManagerTheme.Surface,
                BorderBrush = CameraManagerTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(CameraManagerTheme.CornerRadiusPx),
                Padding = new Thickness(CameraManagerTheme.Spacing)
            };
            StackPanel popupPanel = new StackPanel();
            popupPanel.Children.Add(CameraManagerTheme.SectionTitle("複製選取相機到樓層"));
            popupPanel.Children.Add(_levelCheckList);

            StackPanel popupButtonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button cancelButton = CameraManagerTheme.SecondaryButton("取消");
            cancelButton.Margin = new Thickness(0, 0, 6, 0);
            Button confirmButton = CameraManagerTheme.PrimaryButton("確定");
            popupButtonRow.Children.Add(cancelButton);
            popupButtonRow.Children.Add(confirmButton);
            popupPanel.Children.Add(popupButtonRow);
            popupCard.Child = popupPanel;

            _copyPopup = new Popup
            {
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                Child = popupCard
            };

            cancelButton.Click += (s, e) => _copyPopup.IsOpen = false;
            confirmButton.Click += (s, e) =>
            {
                try
                {
                    List<ElementId> chosen = _levelCheckList.SelectedItems.Cast<LevelOption>().Select(l => l.LevelId).ToList();
                    if (chosen.Count == 0)
                    {
                        MessageBox.Show("請先在上面的清單選取至少一個樓層。", "複製相機", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    _copyPopup.IsOpen = false;
                    CopyToLevelsRequested?.Invoke(chosen);
                }
                catch (Exception ex) { Logger.Error("相機管理面板：複製到其他樓層確定失敗", ex); }
            };
        }

        public void SetLevels(List<LevelOption> levels)
        {
            _levelCheckList.ItemsSource = levels ?? new List<LevelOption>();
        }

        /// <summary>2026-09-24 新增：讓相機列表／地圖標記的右鍵選單「複製到其他樓層…」可以
        /// 直接叫出跟動作列按鈕相同的 Popup，不用重複實作一份選樓層 UI。</summary>
        public void OpenCopyToLevelsPopup(UIElement placementTarget)
        {
            _levelCheckList.SelectedItems.Clear();
            if (placementTarget != null) _copyPopup.PlacementTarget = placementTarget;
            _copyPopup.IsOpen = true;
        }

        public void OpenHeightPopup(UIElement placementTarget)
        {
            if (placementTarget != null) HeightPopup.PlacementTarget = placementTarget;
            HeightPopup.IsOpen = true;
        }

        /// <summary>文件切換／回中性狀態時確保兩個 Popup 都關閉（關閉即釋放其 HWND）。</summary>
        public void CloseAllPopups()
        {
            try { _copyPopup.IsOpen = false; } catch { }
            try { HeightPopup.IsOpen = false; } catch { }
        }
    }
}
