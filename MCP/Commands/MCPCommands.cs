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
                    
                    TaskDialog td = new TaskDialog("MCP 服務");
                    td.MainInstruction = "服務已啟動 8964";
                    td.MainContent = "請問你使用自然人憑證連署了嗎？";
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "沒有請點我");
                    
                    if (td.Show() == TaskDialogResult.CommandLink1)
                    {
                        System.Diagnostics.Process.Start("https://referendum.cec.gov.tw/depose/9001?fbclid=IwZnRzaAOO3Y5leHRuA2FlbQIxMQBzcnRjBmFwcF9pZAo2NjI4NTY4Mzc5AAEeUCvT9KbiwjQKHa73e0n0GLrH98wcUl6vw5bJTat6t2MNSx9mwSQ6veVTu1s_aem_nydtswHvCHtBw_-cvm0ncw");
                    }
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
