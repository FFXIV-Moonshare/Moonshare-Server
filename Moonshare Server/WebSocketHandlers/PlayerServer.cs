    using Moonshare.Server.Managers;
    using Moonshare.Server.Models;
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using WebSocketSharp;
    using WebSocketSharp.Server;

    namespace Moonshare.Server.WebSocketHandlers
    {
        public static class PlayerServer
        {
            private static WebSocketSharp.WebSocket? authServerSocket;
            private static Timer? sessionUpdateTimer;

            public static Task StartAsync()
            {
                var wssv = new WebSocketServer("ws://62.68.75.23:5002");
                wssv.AddWebSocketService<PlayerBehavior>("/player");
                wssv.Start();

                EventLogManager.LogInfo("🎮 PlayerServer running on ws://62.68.75.23:5002/player");

                ConnectAndStartSessionUpdates();
                Console.ReadLine();

                sessionUpdateTimer?.Dispose();
                authServerSocket?.Close();
                wssv.Stop();

                return Task.CompletedTask;
            }

            private static void ConnectAndStartSessionUpdates()
            {
                authServerSocket = new WebSocket("ws://62.68.75.23:5004/sessions");

                authServerSocket.OnOpen += (_, _) =>
                {
                    EventLogManager.LogInfo("Connected to AuthServer /sessions");
                    RequestSessions();
                    StartSessionUpdateTimer();
                };

                authServerSocket.OnMessage += (_, e) =>
                {
                    UpdateSessions(e.Data);
                };

                authServerSocket.OnError += (_, e) =>
                {
                    EventLogManager.LogError("WebSocket error: " + e.Message);
                };

                authServerSocket.OnClose += (_, _) =>
                {
                    EventLogManager.LogError("AuthServer disconnected. Reconnecting in 5s...");
                    sessionUpdateTimer?.Dispose();
                    Task.Delay(5000).ContinueWith(_ => ConnectAndStartSessionUpdates());
                };

                authServerSocket.Connect();
            }

            private static void RequestSessions()
            {
                if (authServerSocket?.ReadyState == WebSocketState.Open)
                    authServerSocket.Send("GET_SESSIONS");
            }

            private static void StartSessionUpdateTimer()
            {
                sessionUpdateTimer = new Timer(_ =>
                {
                    RequestSessions();
                }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            }

            private static void UpdateSessions(string json)
            {
                try
                {
                    var sessions = JsonSerializer.Deserialize<AuthSession[]>(json);
                    if (sessions != null)
                    {
                        SessionManager.Sessions.Clear();
                        foreach (var s in sessions)
                            SessionManager.Sessions[s.SessionToken] = s;

                        EventLogManager.LogInfo($"{sessions.Length} sessions updated from AuthServer.");
                    }
                }
                catch (Exception ex)
                {
                    EventLogManager.LogError("Error updating sessions: " + ex);
                }
            }
        }
    }
