namespace RevitMCP.Contracts
{
    public interface IRevitMcpRuntime
    {
        void Initialize(object uiApplication);
        void StartService();
        void StopService();
        void Shutdown();

        bool IsRunning { get; }
        bool IsConnected { get; }
    }
}
