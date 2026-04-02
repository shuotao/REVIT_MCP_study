using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Core;

namespace RevitMCP.Commands
{
    /// <summary>
    /// 切換 MCP 服務狀態命令 (開/關)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ToggleServiceCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                // 檢查目前狀態
                bool isConnected = Application.IsServiceConnected();

                if (isConnected)
                {
                    // 如果已連線，則停止
                    Application.StopMCPService();
                    Logger.Info("使用者手動停止 MCP 服務");
                    TaskDialog.Show("MCP 服務", "🔴 服務已停止");
                }
                else
                {
                    // 如果未連線，則啟動
                    Logger.Info("使用者手動啟動 MCP 服務");
                    Application.StartMCPService(commandData.Application);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "切換服務狀態失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 重新載入 CoreRuntime 命令（不重啟 Revit）
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ReloadCoreCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                bool loadedBefore = Application.IsCoreLoaded();
                bool runningBefore = Application.IsServiceRunning();

                Application.StartMCPService(commandData.Application);
                bool ok = Application.ReloadCore();

                if (!ok)
                {
                    message = "Core 重載失敗";
                    TaskDialog.Show("Core 重載", "Core 重載失敗，請檢查 log。");
                    return Result.Failed;
                }

                string detail =
                    $"Core 已重載成功\n\n" +
                    $"重載前是否已載入: {(loadedBefore ? "是" : "否")}\n" +
                    $"重載前是否執行中: {(runningBefore ? "是" : "否")}\n" +
                    $"重載後是否執行中: {(Application.IsServiceRunning() ? "是" : "否")}";

                TaskDialog.Show("Core 重載", detail);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "Core 重載失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }


    /// <summary>
    /// 開啟設定視窗命令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var settings = Configuration.ConfigManager.Instance.Settings;
                string info = $"目前設定:\n\n" +
                    $"主機: {settings.Host}\n" +
                    $"埠號: {settings.Port}\n" +
                    $"服務狀態: {(settings.IsEnabled ? "啟用" : "停用")}\n\n" +
                    $"配置檔位置:\n" +
                    $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\RevitMCP\\config.json";
                
                TaskDialog.Show("MCP 設定", info);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "開啟設定失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
