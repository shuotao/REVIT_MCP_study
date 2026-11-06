using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using RevitMCP.UI;

namespace RevitMCP.Commands
{
    /// <summary>
    /// 顯示「相機管理」停靠面板（Phase 1 起改用 RegisterDockablePane，取代舊版無模式浮動視窗
    /// <c>CameraManagerWindow</c>；面板實例在 <see cref="RevitMCP.Application.OnStartup"/> 就已建立好，
    /// 這裡只負責 Show()）。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CameraManagerCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                DockablePane pane = commandData.Application.GetDockablePane(CameraManagerPaneProvider.PaneId);
                if (pane == null)
                {
                    message = "找不到相機管理停靠面板（可能尚未註冊成功）。";
                    TaskDialog.Show("錯誤", message);
                    return Result.Failed;
                }
                pane.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "無法顯示相機管理面板: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
