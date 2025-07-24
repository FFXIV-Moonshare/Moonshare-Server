using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocket = WebSocketSharp.WebSocket;
using WebSocketState = WebSocketSharp.WebSocketState;
using Serilog;

namespace ApiGateway
{
    public static partial class ApiGatewayServer
    {
        private static ConcurrentDictionary<string, AuthSession> _authSessions = new();

        private static WebSocket? _authServerSocket;

        private static async Task MaintainAuthServerConnection()
        {
            while (true)
            {
                try
                {
                    if (_authServerSocket == null || _authServerSocket.ReadyState != WebSocketState.Open)
                    {
                        _authServerSocket = new WebSocket("ws://62.68.75.23:5004/sessions");

                        _authServerSocket.OnOpen += (s, e) =>
                        {
                            Log.Information("AuthServer verbunden.");
                            _authServerSocket.Send("GET_SESSIONS");
                        };

                        _authServerSocket.OnMessage += (s, e) =>
                        {
                            try
                            {
                                var sessions = JsonSerializer.Deserialize<AuthSession[]>(e.Data);
                                if (sessions != null)
                                {
                                    _authSessions.Clear();
                                    foreach (var session in sessions)
                                        _authSessions[session.SessionToken] = session;

                                    Log.Information("Empfangen {Count} Sessions vom AuthServer", _authSessions.Count);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Fehler beim Verarbeiten von Sessions");
                            }
                        };

                        _authServerSocket.OnClose += (s, e) =>
                        {
                            Log.Information("AuthServer Verbindung geschlossen.");
                            _authServerSocket = null;
                        };

                        _authServerSocket.OnError += (s, e) =>
                        {
                            Log.Error("AuthServer Fehler: {Message}", e.Message);
                            _authServerSocket = null;
                        };

                        _authServerSocket.Connect();
                    }

                    if (_authServerSocket?.ReadyState == WebSocketState.Open)
                        _authServerSocket.Send("GET_SESSIONS");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Fehler in AuthServer-Wartung");
                    _authServerSocket = null;
                }

                await Task.Delay(10000);
            }
        }
    }
}
