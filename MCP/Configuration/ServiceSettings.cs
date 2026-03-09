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
        /// 設定檔格式版本（用於偵測舊版設定檔並自動修正）
        /// </summary>
        public int ConfigVersion { get; set; } = 2;

        /// <summary>
        /// 目前設定檔格式版本號
        /// </summary>
        public static int CurrentConfigVersion => 2;

        /// <summary>
        /// 預設 WebSocket 埠號
        /// </summary>
        public const int DefaultPort = 8964;

        /// <summary>
        /// WebSocket 伺服器主機位址
        /// </summary>
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// WebSocket 伺服器埠號
        /// </summary>
        public int Port { get; set; } = DefaultPort;

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

        /// <summary>
        /// 驗證並修正設定值。回傳 true 表示有修正（需存檔）。
        /// </summary>
        public bool ValidateAndFix()
        {
            bool changed = false;

            // 偵測舊版設定檔（沒有 ConfigVersion 欄位時反序列化為 0）
            if (ConfigVersion < CurrentConfigVersion)
            {
                // 舊版設定檔：強制將 Port 修正為預設值
                Port = DefaultPort;
                ConfigVersion = CurrentConfigVersion;
                changed = true;
            }

            // Port 合法範圍檢查
            if (Port < 1024 || Port > 65535)
            {
                Port = DefaultPort;
                changed = true;
            }

            return changed;
        }
    }
}
