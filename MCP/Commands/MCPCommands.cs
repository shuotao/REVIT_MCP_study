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
                bool isConnected = Application.SocketService != null && Application.SocketService.IsConnected;

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

                if (Application.SocketService?.IsRunning == true)
                {
                    var (locked, remote, sinceUtc) = Application.SocketService.GetStatusSnapshot();
                    info += "\n\n" +
                        "連線狀態: " + (locked ? "已鎖定" : "閒置(等待連入)") +
                        ", 目前客戶端: " + (remote ?? "—") +
                        ", 連線時間: " + (sinceUtc.HasValue ? sinceUtc.Value.ToLocalTime().ToString() : "—");
                }

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

    /// <summary>
    /// 切換/釋放目前連線命令：釋放後讓下一個重新連線的 client 取得鎖。
    /// 連線為匿名，無法保證釋放後由哪個 client 取得連線。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SwitchConnectionCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var svc = Application.SocketService;
                if (svc == null || !svc.IsRunning)
                {
                    TaskDialog.Show("切換/釋放連線", "MCP 服務尚未啟動。");
                    return Result.Succeeded;
                }

                var (released, prev) = svc.SwitchConnection();
                TaskDialog.Show("切換/釋放連線", released
                    ? ("已釋放連線（原客戶端 " + prev + "）。\n\n下一個重新連線的客戶端將取得連線。\n無法保證是特定客戶端——若要切換到另一個客戶端，請先讓目前客戶端停止重連。")
                    : "目前沒有已鎖定的連線。下一個連入的客戶端會直接取得連線。");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "切換/釋放連線失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
