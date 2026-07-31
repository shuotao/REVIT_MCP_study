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
        private bool _isRunning;
        private readonly ServiceSettings _settings;
        private CancellationTokenSource _cancellationTokenSource;

        // 連線鎖定狀態 (取代裸 _webSocket)：_connectionLock 保護以下四個欄位，
        // 讓 accept loop / receive task / UI thread (SwitchConnection) 可安全並行存取。
        private readonly object _connectionLock = new object();
        private WebSocket _activeSocket;
        private string _activeRemoteEndpoint;
        private string _activeClientName;   // 來自 ws 連線 query string ?client= (MCP clientInfo.name)
        private DateTime? _connectedAtUtc;
        private DateTime _lastRejectLogUtc = DateTime.MinValue;

        public event EventHandler<RevitCommandRequest> CommandReceived;
        public bool IsRunning => _isRunning;
        public bool IsConnected
        {
            get
            {
                lock (_connectionLock)
                {
                    return _activeSocket != null && _activeSocket.State == WebSocketState.Open;
                }
            }
        }

        /// <summary>
        /// 檢查目前是否已鎖定連線。只能在持有 _connectionLock 時呼叫。
        /// </summary>
        private bool IsLocked_NoLock()
        {
            return _activeSocket != null &&
                (_activeSocket.State == WebSocketState.Open || _activeSocket.State == WebSocketState.CloseReceived);
        }

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
                try
                {
                    var (occupantPid, occupantName) = GetPortOccupant(_settings.Port);
                    if (occupantPid > 0)
                    {
                        Logger.Info($"Port {_settings.Port} 被 {occupantName} (PID: {occupantPid}) 佔用，嘗試自動修復...");

                        if (TryAutoKillPortOccupant(occupantPid, occupantName))
                        {
                            // 等待 Port 釋放
                            await Task.Delay(500);
                            Logger.Info($"已自動結束 {occupantName} (PID: {occupantPid})，Port {_settings.Port} 已釋放");
                        }
                        else if (occupantPid == 4 && await TryReleaseHttpSysPortAsync(_settings.Port))
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
                }
                catch (Exception portCheckEx)
                {
                    // GetActiveTcpListeners() 在部分 Windows 版本會拋出 PlatformNotSupportedException
                    // 跳過 Port 檢查，直接嘗試啟動 HttpListener（若 Port 真的被佔用，Start() 會報錯）
                    Logger.Info($"Port 預檢查不可用（{portCheckEx.GetType().Name}），跳過直接啟動");
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

                // 成功啟動只記 log，不彈 modal TaskDialog：modal 對話框會阻塞 Revit UI 執行緒，
                // 在 Core 熱重載情境下會卡住 ExternalEvent 造成命令 8s timeout（見 docs/core-reload-architecture.md §11）。
                // 啟動結果已於上方 Logger.Info 記錄。收編自 ChimingLu（啟銘）熱重載分支的 UI-thread 安全修正。
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
                        bool locked;
                        lock (_connectionLock)
                        {
                            locked = _settings.ExclusiveLock && IsLocked_NoLock();
                        }

                        if (locked)
                        {
                            // 已有連線鎖定中：在 AcceptWebSocketAsync 之前直接拒絕 (409)，
                            // 不做 101 upgrade。client 端會視為 handshake 失敗，之後每 5 秒自動重試。
                            context.Response.StatusCode = 409;
                            context.Response.Close();
                            RateLimitedRejectLog(context.Request.RemoteEndPoint);
                            continue;
                        }

                        var wsContext = await context.AcceptWebSocketAsync(null);

                        string logClient, logRemote;
                        lock (_connectionLock)
                        {
                            _activeSocket = wsContext.WebSocket;
                            _activeRemoteEndpoint = context.Request.RemoteEndPoint?.ToString();
                            // 客戶端名稱由 node MCP server 以 ?client=<clientInfo.name> 帶入 (匿名時為 unknown)
                            _activeClientName = context.Request.QueryString["client"];
                            _connectedAtUtc = DateTime.UtcNow;
                            logClient = _activeClientName;
                            logRemote = _activeRemoteEndpoint;
                        }

                        Logger.Info("[Socket] MCP Server 已連線 (locked) - client=" + (logClient ?? "unknown") + " " + logRemote);

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
        /// 限流記錄拒絕連線的 log，避免 client 每 5 秒重連造成洗版。
        /// </summary>
        private void RateLimitedRejectLog(System.Net.IPEndPoint remote)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRejectLogUtc).TotalSeconds >= 30)
            {
                _lastRejectLogUtc = now;
                Logger.Info("[Socket] 已拒絕重複連線 (連線已被鎖定) 來源: " + remote + ". 後續拒絕將靜默 30 秒。");
            }
        }

        /// <summary>
        /// 釋放目前鎖定的連線，讓下一個重新連線的 client 取得鎖。
        /// 連線為匿名，無法保證釋放後由哪個 client 取得連線。
        /// </summary>
        public (bool released, string previousRemote) SwitchConnection()
        {
            WebSocket toClose;
            string prev;
            lock (_connectionLock)
            {
                toClose = _activeSocket;
                prev = (string.IsNullOrEmpty(_activeClientName) ? "unknown" : _activeClientName) + " (" + (_activeRemoteEndpoint ?? "?") + ")";
                _activeSocket = null;
                _activeRemoteEndpoint = null;
                _activeClientName = null;
                _connectedAtUtc = null;
            }

            if (toClose == null)
            {
                return (false, null);
            }

            try
            {
                // 必須用 Abort()，不能用 CloseAsync：CloseAsync 需要等待對方回應 close frame，
                // 會和 receive task 既有的 ReceiveAsync 相撞，拋出「already one outstanding receive」例外。
                toClose.Abort();
            }
            catch (Exception ex)
            {
                Logger.Debug("[Socket] Abort 期間例外(可忽略): " + ex.Message);
            }

            Logger.Info("[Socket] 使用者釋放連線: " + prev + ". 下一個重連的客戶端將取得鎖。");
            return (true, prev);
        }

        /// <summary>
        /// 取得目前連線鎖定狀態快照，供 UI (設定視窗) 顯示使用。
        /// </summary>
        public (bool locked, string clientName, string remote, DateTime? sinceUtc) GetStatusSnapshot()
        {
            lock (_connectionLock)
            {
                return (_activeSocket != null && _activeSocket.State == WebSocketState.Open, _activeClientName, _activeRemoteEndpoint, _connectedAtUtc);
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
                    // 大封包接收：累積分段直到 EndOfMessage，避免單次 ReceiveAsync 截斷長訊息
                    using (var ms = new System.IO.MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
                                Logger.Info("[Socket] MCP Server 已斷線");
                                return;
                            }

                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            string message = Encoding.UTF8.GetString(ms.ToArray());
                            Logger.Debug($"[Socket] 接收到訊息 (長度: {message.Length}): {message}");
                            HandleMessage(message);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 這是正常關閉，不需要視為錯誤
                Logger.Info("[Socket] 訊息接收已停止 (服務已取消)");
            }
            catch (WebSocketException ex)
            {
                // 使用者切換/停止造成的中止 (Abort()) 會讓這裡的 ReceiveAsync 拋出例外，屬正常流程
                Logger.Debug("[Socket] 接收訊息中止 (使用者切換/停止造成的中止,屬正常): " + ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Error("[Socket] 接收訊息錯誤", ex);
            }
            finally
            {
                lock (_connectionLock)
                {
                    if (ReferenceEquals(_activeSocket, socket))
                    {
                        _activeSocket = null;
                        _activeRemoteEndpoint = null;
                        _activeClientName = null;
                        _connectedAtUtc = null;
                    }
                }

                // 唯一的 disposer：SwitchConnection() 與 Stop() 只呼叫 Abort()，不在此之外 Dispose，
                // 避免與這裡的 receive loop 重複釋放。
                try
                {
                    socket.Dispose();
                }
                catch
                {
                }
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
            // 鎖內快照後改用區域變數 socket 傳送，避免 IsConnected 檢查與 SendAsync 之間
            // _activeSocket 被 SwitchConnection()/Stop() 換掉或清空 (TOCTOU)。
            WebSocket socket;
            lock (_connectionLock)
            {
                socket = _activeSocket;
            }
            if (socket == null || socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket 未連線");
            }

            try
            {
                string json = JsonConvert.SerializeObject(response);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
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

                // 釋放連線鎖定 (只 Abort，不 CloseAsync/Dispose —— 交給 receive task 的 finally
                // 處理 Dispose，維持單一 disposer 原則)
                WebSocket ws;
                lock (_connectionLock)
                {
                    ws = _activeSocket;
                    _activeSocket = null;
                    _activeRemoteEndpoint = null;
                    _activeClientName = null;
                    _connectedAtUtc = null;
                }

                if (ws != null)
                {
                    try
                    {
                        ws.Abort();
                    }
                    catch
                    {
                    }
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
            bool isInUse;
            try
            {
                isInUse = IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(ep => ep.Port == port);
            }
            catch (PlatformNotSupportedException)
            {
                // 部分 Windows 版本不支援此 API，回傳「未佔用」讓 HttpListener 自行處理
                return (0, null);
            }

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
        private static async Task<bool> TryReleaseHttpSysPortAsync(int port)
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

                await Task.Delay(500);

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

                await Task.Delay(1000);

                psi.Arguments = "start http";
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                }

                await Task.Delay(500);

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
                Logger.Error($"TryReleaseHttpSysPortAsync 失敗: {ex.Message}");
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
