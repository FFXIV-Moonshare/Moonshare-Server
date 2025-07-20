using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using WebSocketSharp.Server;
using Moonshare.Server.Managers;
using Moonshare.Server.WebSocket;

namespace Moonshare.Server.Server
{
    public static class AuthServer
    {
        private static HttpListener? httpListener;

        public static async Task StartAsync()
        {
            var wssv = new WebSocketServer("ws://62.68.75.23:5004");
            wssv.AddWebSocketService<AuthBehavior>("/auth");
            wssv.AddWebSocketService<SessionQueryBehavior>("/sessions");
            wssv.Start();
            Console.WriteLine("✅ WebSocket AuthServer läuft auf ws://62.68.75.23:5004/auth und /sessions");

            httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://62.68.75.23:5003/sessions/");
            httpListener.Start();
            Console.WriteLine("✅ HTTP AuthServer läuft auf http://62.68.75.23:5003/sessions/");

            var cleanupTimer = new System.Timers.Timer(30000);
            cleanupTimer.Elapsed += (_, _) => SessionManager.CleanupExpiredSessions(TimeSpan.FromSeconds(30));
            cleanupTimer.Start();

            _ = Task.Run(async () =>
            {
                while (httpListener.IsListening)
                {
                    try
                    {
                        var ctx = await httpListener.GetContextAsync();
                        ProcessHttpRequest(ctx);
                    }
                    catch (HttpListenerException) { break; }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[HTTP] Fehler: {ex}");
                    }
                }
            });

            Console.WriteLine("Drücke Enter zum Stoppen...");
            Console.ReadLine();

            cleanupTimer.Stop();
            httpListener.Stop();
            wssv.Stop();
        }

        private static void ProcessHttpRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            if (req.HttpMethod != "GET")
            {
                res.StatusCode = 405;
                res.Close();
                return;
            }

            var userId = req.QueryString["userId"];
            if (string.IsNullOrWhiteSpace(userId))
            {
                res.StatusCode = 400;
                var errorBytes = Encoding.UTF8.GetBytes("{\"error\":\"Missing userId parameter\"}");
                res.OutputStream.Write(errorBytes, 0, errorBytes.Length);
                res.Close();
                return;
            }

            var token = SessionManager.GenerateSession(userId!);
            var json = JsonSerializer.Serialize(new { token });
            var bytes = Encoding.UTF8.GetBytes(json);

            res.ContentType = "application/json";
            res.ContentEncoding = Encoding.UTF8;
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();

            Console.WriteLine($"[HTTP] Token für userId '{userId}' ausgegeben.");
        }
    }
}
