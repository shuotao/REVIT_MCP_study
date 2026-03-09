using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RevitMCP.Configuration
{
    /// <summary>
    /// 配置管理器
    /// </summary>
    public class ConfigManager
    {
        private static ConfigManager _instance;
        private static readonly object _lock = new object();
        private readonly string _configPath;

        public ServiceSettings Settings { get; private set; }

        private ConfigManager()
        {
            // 配置檔存放在 AppData\Roaming\RevitMCP
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configDir = Path.Combine(appDataPath, "RevitMCP");
            
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            _configPath = Path.Combine(configDir, "config.json");
            LoadSettings();
        }

        public static ConfigManager Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new ConfigManager();
                    }
                    return _instance;
                }
            }
        }

        /// <summary>
        /// 載入設定
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath, Encoding.UTF8);
                    Settings = JsonConvert.DeserializeObject<ServiceSettings>(json) ?? new ServiceSettings();

                    // 驗證並修正舊版設定檔（例如殘留的錯誤 Port）
                    if (Settings.ValidateAndFix())
                    {
                        SaveSettings();
                    }
                }
                else
                {
                    Settings = new ServiceSettings();
                    SaveSettings();
                }
            }
            catch (Exception)
            {
                Settings = new ServiceSettings();
            }
        }

        /// <summary>
        /// 儲存設定
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(_configPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new Exception($"儲存配置失敗: {ex.Message}");
            }
        }
    }
}
