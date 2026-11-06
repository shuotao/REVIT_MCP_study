using System;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using RevitMCP.Configuration;
using RevitMCP.Core;
using RevitMCP.Tests;

namespace RevitMCP.Tests.SocketOrigin
{
    /// <summary>
    /// Drives the real SocketService from the built RevitMCP.dll and performs two raw
    /// WebSocket handshakes against it: one without an Origin header (what the Node MCP
    /// bridge sends) and one with an Origin header (what any browser page sends).
    ///
    /// Expected after the fix: no-Origin gets 101, Origin gets 403.
    /// Before the fix both get 101, which is the vulnerability.
    ///
    /// ExclusiveLock is disabled so the two handshakes are independent; with the lock on,
    /// the first successful connection would make the second fail with 409 for an
    /// unrelated reason.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Deliberately not 8964: this starts a real listener, so using the production port
        /// would collide with a running add-in and drag its port-conflict handling
        /// (including the process-kill path) into a test run.
        /// </summary>
        private const int Port = 18964;

        private static int Main()
        {
            if (!RevitAssemblies.TryInstallResolver()) return 2;
            return RunAsync().GetAwaiter().GetResult();
        }

        private static async Task<int> RunAsync()
        {
            var settings = new ServiceSettings
            {
                Port = Port,
                Host = "localhost",
                ExclusiveLock = false,
            };

            var service = new SocketService(settings);
            Console.WriteLine($"[harness] starting SocketService on localhost:{Port} ...");
            await service.StartAsync();
            await Task.Delay(700);

            if (!service.IsRunning)
            {
                Console.WriteLine("[harness] FATAL: service did not start");
                return 2;
            }

            var noOrigin = Handshake(null);
            var withOrigin = Handshake("https://evil.example");

            Console.WriteLine();
            Console.WriteLine("  no Origin header : " + noOrigin);
            Console.WriteLine("  Origin header    : " + withOrigin);
            Console.WriteLine();

            bool nodePathOk = noOrigin.StartsWith("101");
            bool browserBlocked = withOrigin.StartsWith("403");

            Console.WriteLine("  node bridge still accepted : " + (nodePathOk ? "PASS" : "FAIL"));
            Console.WriteLine("  browser handshake refused  : " + (browserBlocked ? "PASS" : "FAIL"));

            service.Stop();
            await Task.Delay(300);

            bool green = nodePathOk && browserBlocked;
            Console.WriteLine();
            Console.WriteLine("  RESULT: " + (green ? "GREEN" : "RED"));
            return green ? 0 : 1;
        }

        /// <summary>
        /// Minimal RFC 6455 client handshake over raw TCP. Returns the HTTP status line so
        /// the caller sees exactly what the server answered, rather than a wrapped exception.
        /// </summary>
        private static string Handshake(string origin)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect("127.0.0.1", Port);
                    using (var stream = client.GetStream())
                    {
                        var key = new byte[16];
                        using (var rng = new RNGCryptoServiceProvider())
                        {
                            rng.GetBytes(key);
                        }

                        var sb = new StringBuilder();
                        sb.Append("GET /?client=harness HTTP/1.1\r\n");
                        sb.Append("Host: localhost:").Append(Port).Append("\r\n");
                        sb.Append("Upgrade: websocket\r\n");
                        sb.Append("Connection: Upgrade\r\n");
                        sb.Append("Sec-WebSocket-Key: ").Append(Convert.ToBase64String(key)).Append("\r\n");
                        sb.Append("Sec-WebSocket-Version: 13\r\n");
                        if (origin != null)
                        {
                            sb.Append("Origin: ").Append(origin).Append("\r\n");
                        }
                        sb.Append("\r\n");

                        var request = Encoding.ASCII.GetBytes(sb.ToString());
                        stream.Write(request, 0, request.Length);
                        stream.Flush();

                        stream.ReadTimeout = 5000;
                        var buffer = new byte[1024];
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                        {
                            return "<no response>";
                        }

                        var response = Encoding.ASCII.GetString(buffer, 0, read);
                        var firstLine = response.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                        // "HTTP/1.1 101 Switching Protocols" -> "101 Switching Protocols"
                        var parts = firstLine.Split(new[] { ' ' }, 2);
                        return parts.Length == 2 ? parts[1] : firstLine;
                    }
                }
            }
            catch (Exception ex)
            {
                return "<exception: " + ex.GetType().Name + ": " + ex.Message + ">";
            }
        }
    }
}
