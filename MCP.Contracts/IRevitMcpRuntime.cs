using System;

namespace RevitMCP.Contracts
{
    public interface IRevitMcpRuntime
    {
        void Initialize(object uiApplication);
        void StartService();
        void StopService();
        void Shutdown();

        /// <summary>
        /// 由 Loader 注入的 reload callback，讓 CoreRuntime 在收到 reload_core 命令時可回呼 Loader 執行重載。
        /// </summary>
        void SetReloadCallback(Action reloadCallback);

        bool IsRunning { get; }
        bool IsConnected { get; }
    }
}
