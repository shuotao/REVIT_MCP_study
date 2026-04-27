using System;
using Autodesk.Revit.UI;
using System.Reflection;
using RevitMCP.Core;
using RevitMCP.Configuration;

namespace RevitMCP
{
    public class Application : IExternalApplication
    {
        private static CoreRuntimeManager _runtimeManager;
        private static UIApplication _uiApp;
        private static ExternalEvent _reloadExternalEvent;
        private static ReloadCoreEventHandler _reloadEventHandler;

        public static CoreRuntimeManager RuntimeManager => _runtimeManager;
        public static UIApplication UIApp => _uiApp;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 初始化配置管理器
                _ = ConfigManager.Instance;
                _runtimeManager = new CoreRuntimeManager();
                
                // 建立 Core Reload 的 ExternalEvent（必須在 Startup/UI 執行緒建立）
                _reloadEventHandler = new ReloadCoreEventHandler();
                _reloadExternalEvent = ExternalEvent.Create(_reloadEventHandler);

                // 記錄啟動
                Logger.Info("RevitMCP Plugin 正在啟動...");

                // 建立功能區面板
                RibbonPanel panel = application.CreateRibbonPanel("MCP Tools");
                
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                // 1. MCP 服務切換按鈕 (Toggle)
                PushButtonData toggleButtonData = new PushButtonData(
                    "MCPToggle",
                    "MCP 服務\n(開/關)",
                    assemblyPath,
                    "RevitMCP.Commands.ToggleServiceCommand");
                toggleButtonData.ToolTip = "啟動或停止 MCP WebSocket 服務";
                panel.AddItem(toggleButtonData);

                // 2. 開啟日誌按鈕
                PushButtonData logButtonData = new PushButtonData(
                    "OpenLog",
                    "開啟\n日誌",
                    assemblyPath,
                    "RevitMCP.Commands.OpenLogCommand");
                logButtonData.ToolTip = "開啟 MCP 執行日誌";
                panel.AddItem(logButtonData);

                // 3. 設定按鈕
                PushButtonData settingsButtonData = new PushButtonData(
                    "MCPSettings",
                    "MCP\n設定",
                    assemblyPath,
                    "RevitMCP.Commands.SettingsCommand");
                settingsButtonData.ToolTip = "開啟 MCP 設定視窗";
                panel.AddItem(settingsButtonData);

                // 4. Core 重載按鈕
                PushButtonData reloadButtonData = new PushButtonData(
                    "ReloadCore",
                    "Core\n重載",
                    assemblyPath,
                    "RevitMCP.Commands.ReloadCoreCommand");
                reloadButtonData.ToolTip = "重新載入 RevitMCP.CoreRuntime.dll（不重啟 Revit）";
                panel.AddItem(reloadButtonData);

                // 註冊 Process 退出事件 — Revit crash 時 OnShutdown 不會被呼叫，
                // 但 ProcessExit 仍有機會觸發，確保 HttpListener 被釋放，
                // 避免 HTTP.sys 孤兒 Request Queue 佔住 port。
                AppDomain.CurrentDomain.ProcessExit += (s, e) => EmergencyStopSocket();

                Logger.Info("RevitMCP Plugin 已成功載入");

                TaskDialog.Show("RevitMCP", 
                    "RevitMCP Plugin 已載入\n\n" +
                    "請點擊「MCP 服務 (開/關)」按鈕來啟用 AI 控制功能");
                
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("載入 MCP Tools 失敗", ex);
                TaskDialog.Show("錯誤", "載入 MCP Tools 失敗: " + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                EmergencyStopSocket();
                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
            }
        }

        /// <summary>
        /// 緊急釋放 Socket 服務。同時被 OnShutdown 和 ProcessExit 呼叫，
        /// 內部有 null check 防止重複執行。
        /// </summary>
        private static void EmergencyStopSocket()
        {
            try
            {
                if (_runtimeManager != null)
                {
                    _runtimeManager.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"EmergencyStopSocket failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 啟動 MCP 服務
        /// </summary>
        public static void StartMCPService(UIApplication uiApp)
        {
            try
            {
                _uiApp = uiApp;
                _runtimeManager.SetUIApplication(_uiApp);

                if (_runtimeManager.IsRunning)
                {
                    TaskDialog.Show("MCP 服務", "服務已在執行中。");
                    return;
                }

                _runtimeManager.StartService();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"啟動 MCP 服務失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止 MCP 服務
        /// </summary>
        public static void StopMCPService()
        {
            try
            {
                if (_runtimeManager != null)
                {
                    _runtimeManager.StopService();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("錯誤", $"停止 MCP 服務失敗: {ex.Message}");
            }
        }

        public static bool ReloadCore()
        {
            try
            {
                if (_uiApp == null)
                {
                    return false;
                }

                _runtimeManager.SetUIApplication(_uiApp);
                _runtimeManager.ReloadCore();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ReloadCore 失敗: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 從背景執行緒安全地觸發 Core Reload。
        /// 只呼叫 ExternalEvent.Raise()，實際 ReloadCore() 在 Revit UI 執行緒執行。
        /// </summary>
        public static void RequestReloadCoreFromBackground()
        {
            if (_reloadExternalEvent == null)
            {
                Logger.Error("ReloadExternalEvent 尚未初始化");
                return;
            }
            _reloadEventHandler.SetRuntimeManager(_runtimeManager, _uiApp);
            _reloadExternalEvent.Raise();
        }

        public static bool IsServiceConnected()
        {
            try
            {
                return _runtimeManager != null && _runtimeManager.IsConnected;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsServiceRunning()
        {
            try
            {
                return _runtimeManager != null && _runtimeManager.IsRunning;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsCoreLoaded()
        {
            try
            {
                return _runtimeManager != null && _runtimeManager.IsLoaded;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// ExternalEvent handler for reloading CoreRuntime from a background thread.
    /// Execute() runs on the Revit UI thread, which satisfies ExternalEvent.Create() requirements.
    /// </summary>
    internal class ReloadCoreEventHandler : IExternalEventHandler
    {
        private CoreRuntimeManager _runtimeManager;
        private UIApplication _uiApp;

        public void SetRuntimeManager(CoreRuntimeManager runtimeManager, UIApplication uiApp)
        {
            _runtimeManager = runtimeManager;
            _uiApp = uiApp;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                if (_runtimeManager == null) return;
                _runtimeManager.SetUIApplication(_uiApp ?? app);
                _runtimeManager.ReloadCore();
                Logger.Info("CoreRuntime 熱重載完成（ExternalEvent UI 執行緒）");
            }
            catch (Exception ex)
            {
                Logger.Error($"ReloadCoreEventHandler.Execute 失敗: {ex.Message}", ex);
            }
        }

        public string GetName() => "RevitMCP CoreRuntime Reload";
    }
}
