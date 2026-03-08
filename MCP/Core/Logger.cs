using System;
using System.IO;
using System.Text;

namespace RevitMCP.Core
{
    public static class Logger
    {
        private static readonly string LogDir;
        private static readonly string LogPath;
        private static readonly object Lock = new object();

        public static event Action<string> OnLogMessage;

        static Logger()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                LogDir = Path.Combine(appDataPath, "RevitMCP", "Logs");
                
                if (!Directory.Exists(LogDir))
                {
                    Directory.CreateDirectory(LogDir);
                }

                LogPath = Path.Combine(LogDir, $"RevitMCP_{DateTime.Now:yyyyMMdd}.log");
            }
            catch
            {
                // If we can't initialize the log path, we might want to fall back to a temp dir
                LogDir = Path.GetTempPath();
                LogPath = Path.Combine(LogDir, "RevitMCP_fallback.log");
            }
        }

        public static string GetLogPath() => LogPath;

        public static void Info(string message) => WriteLog("INFO", message);
        public static void Error(string message) => WriteLog("ERROR", message);
        public static void Error(string message, Exception ex) => WriteLog("ERROR", $"{message}\nException: {ex.Message}\nStackTrace: {ex.StackTrace}");
        public static void Debug(string message) => WriteLog("DEBUG", message);

        private static void WriteLog(string level, string message)
        {
            lock (Lock)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string logEntry = $"[{timestamp}] [{level}] {message}";
                    
                    File.AppendAllText(LogPath, logEntry + Environment.NewLine, Encoding.UTF8);
                    
                    // Also write to Debug for Visual Studio output window
                    System.Diagnostics.Debug.WriteLine(logEntry);

                    // Notify subscribers
                    OnLogMessage?.Invoke(logEntry);
                }
                catch
                {
                    // Ignore logging errors to prevent application crash
                }
            }
        }
    }
}
