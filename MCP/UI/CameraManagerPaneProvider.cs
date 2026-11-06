using System;
using Autodesk.Revit.UI;

namespace RevitMCP.UI
{
    /// <summary>
    /// 相機管理停靠面板的註冊資訊。固定 GUID——面板一旦註冊過，之後每次 Revit 啟動都要用
    /// 同一個 Id 才能找到／顯示同一個面板，不可隨意更換。
    /// </summary>
    internal class CameraManagerPaneProvider : IDockablePaneProvider
    {
        /// <summary>固定 GUID，不要改。</summary>
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("A3E1F0B2-7C4D-4E9A-9B0F-6D2C8F4E1A77"));

        private readonly CameraManagerPanelControl _panel;

        public CameraManagerPaneProvider(CameraManagerPanelControl panel)
        {
            _panel = panel;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _panel;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right
            };
            // 2026-09-26 第四輪（使用者：每點一次相機面板就變窄）：Revit API 沒有讀／寫「停靠面板目前寬度」的方法
            // （DockablePane 只有 Show/Hide/IsShown；DockablePaneState 只在註冊時當初始狀態），無法在並排後還原寬度。
            // 能做的：①同一組 4 視圖已開著就不再 TileViews（CameraManagerService.OpenAndTileFourViews）；
            // ②宣告最小寬度 300 px（面板設計下限），讓 Revit 排版不把面板擠到更窄。MinimumWidth 是否也約束 TileViews 未經實機驗證。
            try { data.InitialState.MinimumWidth = 300; }
            catch (Exception ex) { RevitMCP.Core.Logger.Error("相機管理面板：設定停靠面板最小寬度失敗（略過）", ex); }
        }
    }
}
