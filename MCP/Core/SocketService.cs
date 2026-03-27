using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitMCP.Configuration;
using RevitMCP.Models;

namespace RevitMCP.Core
{
    /// <summary>
    /// WebSocket 服務 - 作為伺服器端接收 MCP Server 的連線
    /// </summary>
    public class SocketService
    {
        private HttpListener _httpListener;
        private WebSocket _webSocket;
        private bool _isRunning;
        private readonly ServiceSettings _settings;
        private CancellationTokenSource _cancellationTokenSource;

        public event EventHandler<RevitCommandRequest> CommandReceived;
        public bool IsRunning => _isRunning;
        public bool IsConnected => _webSocket != null && _webSocket.State == WebSocketState.Open;

        public SocketService(ServiceSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// 啟動 WebSocket 伺服器
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
            {
                return;
            }

            try
            {
                // 啟動前檢查 Port 是否被佔用
                var (occupantPid, occupantName) = GetPortOccupant(_settings.Port);
                if (occupantPid > 0)
                {
                    Logger.Info($"Port {_settings.Port} 被 {occupantName} (PID: {occupantPid}) 佔用，嘗試自動修復...");

                    if (TryAutoKillPortOccupant(occupantPid, occupantName))
                    {
                        // 等待 Port 釋放
                        Thread.Sleep(500);
                        Logger.Info($"已自動結束 {occupantName} (PID: {occupantPid})，Port {_settings.Port} 已釋放");
                    }
                    else if (occupantPid == 4 && TryReleaseHttpSysPort(_settings.Port))
                    {
                        // PID 4 = HTTP.sys 孤兒 Request Queue（Revit 異常關閉殘留）
                        Logger.Info($"已透過 netsh 釋放 HTTP.sys 孤兒綁定，Port {_settings.Port} 已釋放");
                    }
                    else
                    {
                        string hint = occupantPid == 4
                            ? $"Port {_settings.Port} 被 HTTP.sys (PID: 4) 佔用（上次異常關閉殘留）。\n\n"
                              + "請以系統管理員身分在終端機執行：\n"
                              + "  net stop http /y && net start http\n\n"
                              + "或執行：scripts\\release-port.ps1"
                            : $"Port {_settings.Port} 被 {occupantName} (PID: {occupantPid}) 佔用，且無法自動修復。\n\n"
                              + "請手動關閉該程式後重試。";
                        Logger.Error(hint);
                        TaskDialog.Show("Revit MCP Plugin - Port 衝突", hint);
                        return;
                    }
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _isRunning = true;

                // 使用 HttpListener 來接受 WebSocket 連線
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{_settings.Port}/");
                _httpListener.Start();

                Logger.Info($"WebSocket 伺服器已啟動 - 監聽: {_settings.Host}:{_settings.Port}");

                // 在背景執行緒中等待連線
                _ = Task.Run(async () => await AcceptConnectionsAsync(_cancellationTokenSource.Token));

                TaskDialog.Show("MCP 服務", $"WebSocket 伺服器已啟動\n監聽: {_settings.Host}:{_settings.Port}");
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Logger.Error("啟動 WebSocket 伺服器失敗", ex);
                TaskDialog.Show("錯誤", $"啟動 WebSocket 伺服器失敗: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 接受 WebSocket 連線
        /// </summary>
        private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    
                    if (context.Request.IsWebSocketRequest)
                    {
                        var wsContext = await context.AcceptWebSocketAsync(null);
                        _webSocket = wsContext.WebSocket;

                        Logger.Info("[Socket] MCP Server 已連線");

                        // 在獨立任務中處理訊息，不要阻塞接受連線的迴圈
                        _ = Task.Run(async () => await ReceiveMessagesAsync(wsContext.WebSocket, cancellationToken));
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Logger.Error("[Socket] 接受連線錯誤", ex);
                    }
                }
            }
        }

        /// <summary>
        /// 接收訊息
        /// </summary>
        private async Task ReceiveMessagesAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Logger.Debug($"[Socket] 接收到訊息: {message}");
                        HandleMessage(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
                        Logger.Info("[Socket] MCP Server 已斷線");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 這是正常關閉，不需要視為錯誤
                Logger.Info("[Socket] 訊息接收已停止 (服務已取消)");
            }
            catch (Exception ex)
            {
                Logger.Error("[Socket] 接收訊息錯誤", ex);
            }
        }

        /// <summary>
        /// 處理接收到的訊息
        /// </summary>
        private void HandleMessage(string message)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<RevitCommandRequest>(message);
                Logger.Info($"[Socket] 處理命令: {request.CommandName} (RequestId: {request.RequestId})");
                CommandReceived?.Invoke(this, request);
            }
            catch (Exception ex)
            {
                Logger.Error($"[Socket] 解析命令失敗: {message}", ex);
            }
        }

        /// <summary>
        /// 發送回應
        /// </summary>
        public async Task SendResponseAsync(RevitCommandResponse response)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("WebSocket 未連線");
            }

            try
            {
                string json = JsonConvert.SerializeObject(response);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                Logger.Debug($"[Socket] 已發送回應 (RequestId: {response.RequestId})");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Socket] 發送回應失敗 (RequestId: {response.RequestId})", ex);
                throw;
            }
        }

        /// <summary>
        /// 停止服務
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            Logger.Info("正在停止 WebSocket 伺服器...");

            try
            {
                // 先取消所有背景任務
                _cancellationTokenSource?.Cancel();

                // 處理 WebSocket 關閉 (不阻塞 UI 執行緒)
                if (_webSocket != null)
                {
                    var ws = _webSocket;
                    _webSocket = null; // 先斷開引用

                    Task.Run(async () =>
                    {
                        try
                        {
                            if (ws.State == WebSocketState.Open)
                            {
                                // 給予 2 秒時間嘗試正常關閉
                                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                                {
                                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "服務關閉", cts.Token);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"WebSocket 正常關閉失敗 (此為正常現象): {ex.Message}");
                        }
                        finally
                        {
                            ws.Dispose();
                            Logger.Info("WebSocket 已釋放");
                        }
                    });
                }

                // 停止 HttpListener
                if (_httpListener != null && _httpListener.IsListening)
                {
                    _httpListener.Stop();
                    _httpListener.Close();
                    Logger.Info("HttpListener 已停止並關閉");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("停止服務時發生錯誤", ex);
            }
            finally
            {
                _isRunning = false;
                Logger.Info("WebSocket 伺服器已完全停止");
            }
        }

        /// <summary>
        /// 檢查指定 Port 是否被佔用，回傳 (PID, 進程名稱)。未佔用則回傳 (0, null)。
        /// </summary>
        private static (int pid, string name) GetPortOccupant(int port)
        {
            bool isInUse = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(ep => ep.Port == port);

            if (!isInUse)
                return (0, null);

            // Port 被佔用，透過 netstat 找出佔用者 PID
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    var lines = output.Split('\n');
                    string portPattern = $":{port} ";
                    foreach (string line in lines)
                    {
                        // 不依賴語系關鍵字，改為判斷 port 格式 + TCP 行結構
                        if (!line.Contains(portPattern)) continue;

                        string trimmed = line.Trim();
                        string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        // netstat -ano 格式: Proto  Local Address  Foreign Address  State  PID
                        // PID 固定在最後一欄
                        if (parts.Length >= 5 && int.TryParse(parts[parts.Length - 1], out int pid) && pid > 0)
                        {
                            try
                            {
                                var occupant = Process.GetProcessById(pid);
                                return (pid, occupant.ProcessName);
                            }
                            catch
                            {
                                return (pid, "unknown");
                            }
                        }
                    }
                }
            }
            catch
            {
                // netstat 失敗
            }

            return (-1, "unknown");
        }

        /// <summary>
        /// 嘗試透過 netsh 釋放 HTTP.sys 孤兒 Request Queue。
        /// 當 PID 4 (System) 佔住 port 時，代表 HttpListener 上次沒有正常關閉，
        /// HTTP.sys kernel driver 仍持有該 port 的綁定。
        /// </summary>
        private static bool TryReleaseHttpSysPort(int port)
        {
            try
            {
                // 嘗試刪除可能殘留的 URL ACL 保留
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"http delete urlacl url=http://localhost:{port}/",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(5000);
                }

                // 也嘗試 http://+:port/ 格式
                psi.Arguments = $"http delete urlacl url=http://+:{port}/";
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(5000);
                }

                Thread.Sleep(500);

                // 檢查是否已釋放
                bool stillInUse = IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(ep => ep.Port == port);

                if (!stillInUse)
                {
                    Logger.Info($"netsh urlacl 清除成功，Port {port} 已釋放");
                    return true;
                }

                // urlacl 清除不夠，嘗試 net stop http（需要管理員權限）
                Logger.Info("urlacl 清除後 port 仍被佔用，嘗試重啟 HTTP 服務...");

                psi = new ProcessStartInfo
                {
                    FileName = "net",
                    Arguments = "stop http /y",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                }

                Thread.Sleep(1000);

                psi.Arguments = "start http";
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                }

                Thread.Sleep(500);

                stillInUse = IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(ep => ep.Port == port);

                if (!stillInUse)
                {
                    Logger.Info($"HTTP 服務重啟成功，Port {port} 已釋放");
                    return true;
                }

                Logger.Error($"HTTP 服務重啟後 Port {port} 仍被佔用");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"TryReleaseHttpSysPort 失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 嘗試自動結束佔用 Port 的進程。
        /// 只會結束 node / Revit 相關的殭屍進程，不會誤殺其他應用程式。
        /// </summary>
        private static bool TryAutoKillPortOccupant(int pid, string processName)
        {
            if (pid <= 0) return false;

            string lower = (processName ?? "").ToLowerInvariant();

            // 安全白名單：只自動結束 MCP 相關的殭屍進程
            bool isSafeToKill = lower.Contains("node")
                             || lower.Contains("revitmcp");

            if (!isSafeToKill)
            {
                Logger.Info($"進程 {processName} (PID: {pid}) 不在自動清除白名單中，跳過");
                return false;
            }

            try
            {
                var proc = Process.GetProcessById(pid);
                proc.Kill();
                proc.WaitForExit(3000);
                Logger.Info($"已自動結束進程: {processName} (PID: {pid})");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"無法結束進程 {processName} (PID: {pid}): {ex.Message}");
                return false;
            }
        }
    }
}
