using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Timers;
using WebSocketSharp;
using Moonshare.Server.Models;
using Serilog;
using Timer = System.Timers.Timer;

namespace Moonshare.Server.Managers
{
    public static class PlayerSessionManager
    {
        private static WebSocketSharp.WebSocket? _authServerSocket;
        private static Timer? _syncTimer;

        // Vom AuthServer synchronisierte Sessions
        public static Dictionary<string, AuthSession> SyncedSessions { get; private set; } = new();

        public static void StartSync()
        {
            _authServerSocket = new WebSocketSharp.WebSocket("ws://localhost:5004/sessions");

            _authServerSocket.OnOpen += (_, _) =>
            {
                Log.Information("[PlayerServer] Connected to AuthServer for session sync.");
                _authServerSocket.Send("GET_SESSIONS");
            };

            _authServerSocket.OnMessage += (_, e) =>
            {
                try
                {
                    var sessions = JsonSerializer.Deserialize<List<AuthSession>>(e.Data);
                    if (sessions != null)
                    {
                        var updated = new Dictionary<string, AuthSession>();
                        foreach (var session in sessions)
                            updated[session.SessionToken] = session;

                        SyncedSessions = updated;
                        Log.Information("[PlayerServer] Synced {Count} sessions.", SyncedSessions.Count);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[PlayerServer] Failed to parse session data.");
                }
            };

            _authServerSocket.OnError += (_, e) =>
            {
                Log.Error("[PlayerServer] AuthServer sync error: {Message}", e.Message);
            };

            _authServerSocket.OnClose += (_, _) =>
            {
                Log.Warning("[PlayerServer] AuthServer connection closed.");
            };

            try
            {
                _authServerSocket.Connect();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PlayerServer] Connection to AuthServer failed.");
            }

            _syncTimer = new Timer(10000); // alle 10 Sekunden synchronisieren
            _syncTimer.Elapsed += (_, _) =>
            {
                if (_authServerSocket?.IsAlive == true)
                {
                    _authServerSocket.Send("GET_SESSIONS");
                }
                else
                {
                    Log.Warning("[PlayerServer] AuthServer socket is not alive, trying to reconnect...");
                    try
                    {
                        _authServerSocket.Connect();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[PlayerServer] Reconnect failed.");
                    }
                }
            };
            _syncTimer.Start();
        }
    }
}
