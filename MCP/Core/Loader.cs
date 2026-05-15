using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;

namespace RevitMCPLoader
{
    public class App : IExternalApplication
    {
        private static string CoreDllPath = @"c:\Users\sn698\Desktop\REVIT_MCP_study\MCP\bin\Debug.R23\RevitMCP.dll";
        private IExternalApplication _realApp;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 初始化 Ribbon 面板 (由 Loader 負責，這樣按鈕路徑永遠固定)
                RibbonPanel panel = application.CreateRibbonPanel("MCP Tools");
                string loaderPath = Assembly.GetExecutingAssembly().Location;

                // 使用加載器自己的路徑建立按鈕，但指向 Core 的類別
                PushButtonData toggleBtn = new PushButtonData("MCPToggle", "MCP 服務\n(智慧重載)", loaderPath, "RevitMCPLoader.ReloadAndExecuteCommand");
                toggleBtn.ToolTip = "點擊此按鈕將自動讀取最新代碼並切換服務狀態";
                panel.AddItem(toggleBtn);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MCP Loader Error", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ReloadAndExecuteCommand : IExternalCommand
    {
        private static string CoreDllPath = @"c:\Users\sn698\Desktop\REVIT_MCP_study\MCP\bin\Debug.R23\RevitMCP.dll";
        
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "RevitMCP_HotReload");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                string tempDllPath = Path.Combine(tempDir, "RevitMCP_Live.dll");
                
                File.Copy(CoreDllPath, tempDllPath, true);
                Assembly assembly = Assembly.Load(File.ReadAllBytes(tempDllPath)); // 徹底不鎖定
                
                Type cmdType = assembly.GetType("RevitMCP.Commands.ToggleServiceCommand");
                if (cmdType != null)
                {
                    IExternalCommand cmd = (IExternalCommand)Activator.CreateInstance(cmdType);
                    return cmd.Execute(commandData, ref message, elements);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Hot Reload Error", ex.Message);
            }
            return Result.Failed;
        }
    }
}
