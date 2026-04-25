using System;
using Autodesk.Revit.UI;
using RevitMCP.Configuration;
using RevitMCP.Contracts;
using RevitMCP.Core;

namespace RevitMCP.CoreRuntime
{
    public class RevitMcpCoreRuntime : IRevitMcpRuntime
    {
        private SocketService _socketService;
        private UIApplication _uiApplication;
        private Action _reloadCallback;

        public bool IsRunning => _socketService != null && _socketService.IsRunning;
        public bool IsConnected => _socketService != null && _socketService.IsConnected;

        public void Initialize(object uiApplication)
        {
            if (uiApplication is not UIApplication app)
            {
                throw new ArgumentException("uiApplication must be Autodesk.Revit.UI.UIApplication");
            }

            _uiApplication = app;
            _ = ConfigManager.Instance;
            _ = ExternalEventManager.Instance;
            Logger.Info("CoreRuntime 初始化完成");
        }

        public void StartService()
        {
            if (_uiApplication == null)
            {
                throw new InvalidOperationException("CoreRuntime 尚未 Initialize");
            }

            if (_socketService != null && _socketService.IsRunning)
            {
                Logger.Info("CoreRuntime: 服務已在執行中");
                return;
            }

            var settings = ConfigManager.Instance.Settings;
            _socketService = new SocketService(settings);
            _socketService.CommandReceived += OnCommandReceived;
            _socketService.StartAsync().GetAwaiter().GetResult();

            settings.IsEnabled = true;
            ConfigManager.Instance.SaveSettings();
        }

        public void StopService()
        {
            if (_socketService == null)
            {
                return;
            }

            _socketService.CommandReceived -= OnCommandReceived;
            _socketService.Stop();
            _socketService = null;

            var settings = ConfigManager.Instance.Settings;
            settings.IsEnabled = false;
            ConfigManager.Instance.SaveSettings();
        }

        public void Shutdown()
        {
            StopService();
        }

        public void SetReloadCallback(Action reloadCallback)
        {
            _reloadCallback = reloadCallback;
        }

        private void OnCommandReceived(object sender, Models.RevitCommandRequest request)
        {
            // reload_core 由 Loader 負責執行，CoreRuntime 透過 callback 回呼
            if (request.CommandName == "reload_core")
            {
                if (_reloadCallback == null)
                {
                    var errResp = new Models.RevitCommandResponse
                    {
                        Success = false,
                        Error = "ReloadCallback 尚未設定（Loader 版本過舊？）",
                        RequestId = request.RequestId
                    };
                    _socketService?.SendResponseAsync(errResp).ConfigureAwait(false);
                    return;
                }

                // 先送回 ACK，再非同步觸發 reload
                // reload 會呼叫 StopService()，必須等 response 送出後才能執行
                var ackResp = new Models.RevitCommandResponse
                {
                    Success = true,
                    Data = new { Message = "CoreRuntime 熱重載已觸發，請稍後確認版本" },
                    RequestId = request.RequestId
                };
                _socketService?.SendResponseAsync(ackResp).GetAwaiter().GetResult();

                var cb = _reloadCallback;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(200);
                    try { cb(); } catch { }
                });
                return;
            }

            ExternalEventManager.Instance.ExecuteCommand((uiApp) =>
            {
                try
                {
                    var executor = new CommandExecutor(uiApp);
                    var response = executor.ExecuteCommand(request);
                    _socketService?.SendResponseAsync(response).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var errorResponse = new Models.RevitCommandResponse
                    {
                        Success = false,
                        Error = ex.Message,
                        RequestId = request.RequestId
                    };

                    _socketService?.SendResponseAsync(errorResponse).ConfigureAwait(false);
                }
            });
        }
    }
}
