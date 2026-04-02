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

        private void OnCommandReceived(object sender, Models.RevitCommandRequest request)
        {
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
