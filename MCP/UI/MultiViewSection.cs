using System;
using System.Windows;
using System.Windows.Controls;

namespace RevitMCP.UI
{
    /// <summary>
    /// 4.9 A：並排檢視／還原——開啟該相機的透視圖＋所在樓層的既有平面，執行 Revit 內建
    /// 「並排視窗」(WT)；還原只關掉這次額外開的視圖，回到原本狀態。放在動作列
    /// （緊接在「一鍵 4 視圖」旁）。
    ///
    /// 2026-09-24：面板拿掉分頁後，4.9 B「面板內嵌 PreviewControl」實驗區塊整段移除
    /// （規格改為只保留「開啟預覽視窗」這一個入口，見 <see cref="CameraManagerPanelControl"/>
    /// 標題列的圖示按鈕，直接呼叫既有的 <c>OpenPreviewWindow()</c>，不再經過這個類別）。
    /// 這個類別現在只剩並排／還原兩顆按鈕，本身完全不呼叫任何 Revit API，全部透過事件
    /// 交回面板控制。
    /// </summary>
    internal class MultiViewSection
    {
        private readonly Button _sideBySideButton, _restoreButton;

        public FrameworkElement SideBySideBar { get; }

        public event Action SideBySideRequested;
        public event Action RestoreRequested;

        public MultiViewSection()
        {
            StackPanel sideBySidePanel = new StackPanel { Orientation = Orientation.Horizontal };
            _sideBySideButton = CameraManagerTheme.SecondaryButton("並排");
            _sideBySideButton.ToolTip = "④ 並排：開啟選取相機的透視圖＋所在樓層的既有平面視圖（先關閉其他視圖），並執行 Revit「並排視窗」";
            _sideBySideButton.Margin = new Thickness(0, 0, CameraManagerTheme.Spacing, 0);
            _sideBySideButton.Click += (s, e) => SideBySideRequested?.Invoke();
            sideBySidePanel.Children.Add(_sideBySideButton);

            _restoreButton = CameraManagerTheme.SecondaryButton("還原");
            _restoreButton.ToolTip = "④ 還原：關閉並排時額外開啟的視圖、切回之前的作用視圖（並排前被關掉的其他視圖不會重新打開）";
            _restoreButton.IsEnabled = false;
            _restoreButton.Click += (s, e) => RestoreRequested?.Invoke();
            sideBySidePanel.Children.Add(_restoreButton);
            SideBySideBar = sideBySidePanel;
        }

        public void SetRestoreEnabled(bool enabled) => _restoreButton.IsEnabled = enabled;
    }
}
