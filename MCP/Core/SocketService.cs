using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    /// WebSocket 服務 - 使用 TcpListener 取代 HttpListener，
    /// 避免 HTTP.sys 孤兒 Request Queue 導致 Revit crash 後需要重開機。
    /// TcpListener 由 Revit process 持有，crash 時 OS 自動釋放 port。
    /// </summary>
    public class SocketService
    {
        private TcpListener _tcpListener;
        private Stream _clientStream;
        private TcpClient _tcpClient;
        private bool _isRunning;
        private bool _isConnected;
        private readonly ServiceSettings _settings;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public event EventHandler<RevitCommandRequest> CommandReceived;
        public bool IsRunning => _isRunning;
        public bool IsConnected => _isConnected;

        public SocketService(ServiceSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task StartAsync()
        {
            if (_isRunning) return;

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _tcpListener = new TcpListener(IPAddress.Any, _settings.Port);
                _tcpListener.Start();
                _isRunning = true;
                Logger.Info($"WebSocket 伺服器已啟動 - 監聽: {_settings.Host}:{_settings.Port}");
                _ = Task.Run(async () => await AcceptConnectionsAsync(_cancellationTokenSource.Token));
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                _isRunning = false;
                Logger.Error($"Port {_settings.Port} 已被佔用", ex);
                TaskDialog.Show("Revit MCP Plugin - Port 衝突",
                    $"Port {_settings.Port} 已被佔用。\n\n請確認沒有其他程式占用此 Port 後重試。");
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Logger.Error("啟動 WebSocket 伺服器失敗", ex);
                TaskDialog.Show("錯誤", $"啟動 WebSocket 伺服器失敗: {ex.Message}");
                throw;
            }

            await Task.CompletedTask;
        }

        private async Task AcceptConnectionsAsync(CancellationToken ct)
        {
            while (_isRunning && !ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync();
                    _ = Task.Run(async () => await HandleClientAsync(client, ct));
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) when (_isRunning)
                {
                    Logger.Error("[Socket] 接受連線錯誤", ex);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();

                if (!await PerformHandshakeAsync(stream, ct))
                {
                    Logger.Info("[Socket] WebSocket 握手失敗，關閉連線");
                    client.Close();
                    return;
                }

                _tcpClient = client;
                _clientStream = stream;
                _isConnected = true;
                Logger.Info("[Socket] MCP Server 已連線");

                await ReceiveMessagesAsync(stream, ct);

                _isConnected = false;
                _clientStream = null;
                _tcpClient = null;
                Logger.Info("[Socket] MCP Server 已斷線");
            }
            catch (Exception ex) when (_isRunning)
            {
                _isConnected = false;
                Logger.Error("[Socket] 客戶端處理錯誤", ex);
            }
        }

        private static async Task<bool> PerformHandshakeAsync(Stream stream, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var buf = new byte[1];
            while (true)
            {
                int n = await stream.ReadAsync(buf, 0, 1, ct);
                if (n == 0) return false;
                sb.Append((char)buf[0]);
                if (sb.Length > 8192) return false;
                if (sb.Length >= 4
                    && sb[sb.Length - 4] == '\r' && sb[sb.Length - 3] == '\n'
                    && sb[sb.Length - 2] == '\r' && sb[sb.Length - 1] == '\n') break;
            }

            string wsKey = null;
            foreach (string line in sb.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    wsKey = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                    break;
                }
            }
            if (wsKey == null) return false;

            string accept;
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(
                    Encoding.ASCII.GetBytes(wsKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
                accept = Convert.ToBase64String(hash);
            }

            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n";

            byte[] bytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(bytes, 0, bytes.Length, ct);
            await stream.FlushAsync(ct);
            return true;
        }

        private async Task ReceiveMessagesAsync(Stream stream, CancellationToken ct)
        {
            while (_isRunning && !ct.IsCancellationRequested)
            {
                try
                {
                    var (opcode, payload) = await ReadFrameAsync(stream, ct);

                    if (opcode == 0x1 || opcode == 0x2)
                    {
                        string message = Encoding.UTF8.GetString(payload);
                        Logger.Debug($"[Socket] 接收到訊息: {message}");
                        HandleMessage(message);
                    }
                    else if (opcode == 0x8)
                    {
                        Logger.Info("[Socket] 收到 Close frame");
                        await WriteFrameAsync(stream, 0x8, new byte[0], CancellationToken.None);
                        break;
                    }
                    else if (opcode == 0x9)
                    {
                        await WriteFrameAsync(stream, 0xA, payload, ct);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (EndOfStreamException) { break; }
                catch (IOException) { break; }
                catch (Exception ex) when (_isRunning)
                {
                    Logger.Error("[Socket] 接收訊息錯誤", ex);
                    break;
                }
            }
        }

        private static async Task<(int opcode, byte[] payload)> ReadFrameAsync(Stream stream, CancellationToken ct)
        {
            byte[] header = await ReadExactAsync(stream, 2, ct);
            int opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            long payloadLen = header[1] & 0x7F;

            if (payloadLen == 126)
            {
                byte[] ext = await ReadExactAsync(stream, 2, ct);
                payloadLen = (ext[0] << 8) | ext[1];
            }
            else if (payloadLen == 127)
            {
                byte[] ext = await ReadExactAsync(stream, 8, ct);
                payloadLen = 0;
                for (int i = 0; i < 8; i++) payloadLen = (payloadLen << 8) | ext[i];
            }

            byte[] maskKey = masked ? await ReadExactAsync(stream, 4, ct) : null;
            byte[] payload = await ReadExactAsync(stream, (int)payloadLen, ct);

            if (masked && maskKey != null)
                for (int i = 0; i < payload.Length; i++) payload[i] ^= maskKey[i % 4];

            return (opcode, payload);
        }

        private static async Task WriteFrameAsync(Stream stream, int opcode, byte[] payload, CancellationToken ct)
        {
            int len = payload.Length;
            byte[] header;

            if (len < 126)
                header = new byte[] { (byte)(0x80 | opcode), (byte)len };
            else if (len <= 65535)
                header = new byte[] { (byte)(0x80 | opcode), 126, (byte)(len >> 8), (byte)(len & 0xFF) };
            else
            {
                header = new byte[10];
                header[0] = (byte)(0x80 | opcode);
                header[1] = 127;
                for (int i = 0; i < 8; i++) header[2 + i] = (byte)((len >> (56 - 8 * i)) & 0xFF);
            }

            byte[] frame = new byte[header.Length + len];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            Buffer.BlockCopy(payload, 0, frame, header.Length, len);
            await stream.WriteAsync(frame, 0, frame.Length, ct);
            await stream.FlushAsync(ct);
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken ct)
        {
            if (count == 0) return new byte[0];
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buf, read, count - read, ct);
                if (n == 0) throw new EndOfStreamException("Connection closed unexpectedly");
                read += n;
            }
            return buf;
        }

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

        public async Task SendResponseAsync(RevitCommandResponse response)
        {
            if (!_isConnected || _clientStream == null)
                throw new InvalidOperationException("WebSocket 未連線");

            await _sendLock.WaitAsync();
            try
            {
                string json = JsonConvert.SerializeObject(response);
                byte[] payload = Encoding.UTF8.GetBytes(json);
                await WriteFrameAsync(_clientStream, 0x1, payload, CancellationToken.None);
                Logger.Debug($"[Socket] 已發送回應 (RequestId: {response.RequestId})");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _isConnected = false;
            Logger.Info("正在停止 WebSocket 伺服器...");

            try
            {
                _cancellationTokenSource?.Cancel();
                _tcpListener?.Stop();
                try { _clientStream?.Close(); } catch { }
                try { _tcpClient?.Close(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Error("停止服務時發生錯誤", ex);
            }
            finally
            {
                Logger.Info("WebSocket 伺服器已完全停止");
            }
        }
    }
}