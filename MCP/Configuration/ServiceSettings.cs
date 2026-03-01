using System;

namespace RevitMCP.Configuration
{
    /// <summary>
    /// MCP 服務設定
    /// </summary>
    [Serializable]
    public class ServiceSettings
    {
        /// <summary>
        /// WebSocket 伺服器主機位址
        /// </summary>
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// WebSocket 伺服器埠號
        /// </summary>
        public int Port { get; set; } = 8964;

        /// <summary>
        /// 是否啟用 MCP 服務
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// 自動重連間隔（毫秒）
        /// </summary>
        public int ReconnectInterval { get; set; } = 5000;

        /// <summary>
        /// 命令執行逾時時間（毫秒）
        /// </summary>
        public int CommandTimeout { get; set; } = 30000;
    }
}
