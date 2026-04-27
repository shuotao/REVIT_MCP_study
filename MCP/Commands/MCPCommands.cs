using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Net.Sockets;
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
                // 以 IsServiceRunning() 判斷開關狀態（而非 IsServiceConnected，
                // 後者依賴外部 MCP Server 已連線，會導致需按兩次才生效）
                bool isRunning = Application.IsServiceRunning();

                if (isRunning)
                {
                    // 如果服務執行中，則停止
                    Application.StopMCPService();
                    Logger.Info("使用者手動停止 MCP 服務");
                    TaskDialog.Show("MCP 服務", "🔴 服務已停止");
                }
                else
                {
                    // 如果服務未執行，則啟動
                    Logger.Info("使用者手動啟動 MCP 服務");
                    Application.StartMCPService(commandData.Application);
                    TaskDialog.Show("MCP 服務", "🟢 服務已啟動");
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
        private static bool IsPortListening(string host, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    return connectTask.Wait(TimeSpan.FromMilliseconds(300)) && client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var settings = Configuration.ConfigManager.Instance.Settings;

                bool isRunning = Application.IsServiceRunning();
                bool isConnected = Application.IsServiceConnected();
                bool portListening = IsPortListening(settings.Host, settings.Port);

                string serviceStatus;
                if (isConnected)
                {
                    serviceStatus = "啟用（已連線）";
                }
                else if (isRunning)
                {
                    serviceStatus = "啟用（待連線）";
                }
                else if (portListening)
                {
                    serviceStatus = "啟用（埠已監聽）";
                }
                else
                {
                    serviceStatus = "停用";
                }

                string info = $"目前設定:\n\n" +
                    $"主機: {settings.Host}\n" +
                    $"埠號: {settings.Port}\n" +
                    $"服務狀態: {serviceStatus}\n" +
                    $"Runtime.IsRunning: {(isRunning ? "是" : "否")}\n" +
                    $"Runtime.IsConnected: {(isConnected ? "是" : "否")}\n" +
                    $"Port監聽: {(portListening ? "是" : "否")}\n\n" +
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
