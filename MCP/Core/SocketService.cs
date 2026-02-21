using System;
using System.Net;
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
                _cancellationTokenSource = new CancellationTokenSource();
                _isRunning = true;

                // 使用 HttpListener 來接受 WebSocket 連線
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://{_settings.Host}:{_settings.Port}/");
                _httpListener.Start();

                Logger.Info($"WebSocket 伺服器已啟動 - 監聽: {_settings.Host}:{_settings.Port}");
                TaskDialog.Show("MCP 服務", $"WebSocket 伺服器已啟動\n監聽: {_settings.Host}:{_settings.Port}");

                // 在背景執行緒中等待連線
                _ = Task.Run(async () => await AcceptConnectionsAsync(_cancellationTokenSource.Token));
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

                        // 開始接收訊息
                        await ReceiveMessagesAsync(cancellationToken);
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
        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            try
            {
                while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Logger.Debug($"[Socket] 接收到訊息: {message}");
                        HandleMessage(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
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
    }
}
