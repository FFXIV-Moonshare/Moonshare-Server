using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Serilog;
using WebSocketSharp.Server;
using Moonshare.Server.Managers;
using Moonshare.Server.WebSocket;

namespace Moonshare.Server.Server
{
    public static class AuthServer
    {
        private static HttpListener? _httpListener;
        private static System.Timers.Timer? _cleanupTimer;
        private static WebSocketServer? _webSocketServer;
        private static CancellationTokenSource? _cts;

        private const string WebSocketUrl = "ws://62.68.75.23:5004";
        private const string HttpUrl = "http://62.68.75.23:5003/sessions/";

        public static async Task StartAsync()
        {
            _cts = new CancellationTokenSource();

            try
            {
                _webSocketServer = new WebSocketServer(WebSocketUrl);
                _webSocketServer.AddWebSocketService<AuthBehavior>("/auth");
                _webSocketServer.AddWebSocketService<SessionQueryBehavior>("/sessions");
                _webSocketServer.Start();
                Log.Information("✅ WebSocket AuthServer läuft auf {Url}/auth und /sessions", WebSocketUrl);

                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add(HttpUrl);
                _httpListener.Start();
                Log.Information("✅ HTTP AuthServer läuft auf {Url}", HttpUrl);

                _cleanupTimer = new System.Timers.Timer(30_000);
                _cleanupTimer.Elapsed += (s, e) => SessionManager.CleanupInactiveSessions();
                _cleanupTimer.Start();

                await ListenHttpAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "❌ Fehler beim Start des AuthServers");
            }

            await StopAsync();
        }

        private static async Task ListenHttpAsync(CancellationToken cancellationToken)
        {
            while (_httpListener?.IsListening == true && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => ProcessHttpRequestAsync(ctx), cancellationToken);
                }
                catch (HttpListenerException)
                {
                    break; // normal on shutdown
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[HTTP] Fehler beim Empfangen von HTTP-Anfragen");
                }
            }
        }

        private static async Task ProcessHttpRequestAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            try
            {
                if (req.HttpMethod != "GET")
                {
                    res.StatusCode = 405;
                    Log.Warning("[HTTP] Methode nicht erlaubt: {Method}", req.HttpMethod);
                    return;
                }

                var userId = req.QueryString["userId"];
                if (string.IsNullOrWhiteSpace(userId))
                {
                    res.StatusCode = 400;
                    var errorBytes = Encoding.UTF8.GetBytes("{\"error\":\"Missing userId parameter\"}");
                    await res.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                    Log.Warning("[HTTP] Anfrage ohne userId erhalten von {IP}", req.RemoteEndPoint?.Address);
                    return;
                }

                var clientAddress = req.RemoteEndPoint?.Address.ToString() ?? "unknown";

                var token = SessionManager.GenerateSession(userId!, clientAddress);
                var json = JsonSerializer.Serialize(new { token });
                var bytes = Encoding.UTF8.GetBytes(json);

                res.ContentType = "application/json";
                res.ContentEncoding = Encoding.UTF8;
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);

                Log.Information("[HTTP] Session-Token für userId '{UserId}' ausgegeben an {IP}", userId, clientAddress);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[HTTP] Fehler beim Verarbeiten der Anfrage");
                res.StatusCode = 500;
            }
            finally
            {
                res.Close();
            }
        }

        public static async Task StopAsync()
        {
            try
            {
                _cts?.Cancel();

                _cleanupTimer?.Stop();
                _cleanupTimer?.Dispose();
                _cleanupTimer = null;

                if (_httpListener?.IsListening == true)
                {
                    _httpListener.Stop();
                }
                _httpListener?.Close();
                _httpListener = null;

                _webSocketServer?.Stop();
                _webSocketServer = null;

                Log.Information("🛑 AuthServer wurde gestoppt.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Fehler beim Stoppen des AuthServers");
            }

            await Task.CompletedTask;
        }
    }
}
