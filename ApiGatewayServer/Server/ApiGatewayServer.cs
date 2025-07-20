using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;

public static class ApiGatewayServer
{
    private static HttpListener? _listener;
    private static ConcurrentDictionary<string, AuthSession> _authSessions = new();
    private static int _playerCount = 0;

    private static WebSocket? _authServerSocket;
    private static WebSocket? _playerServerSocket;

    public static async Task StartAsync()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://62.68.75.23:8090/");
        _listener.Start();

        Console.WriteLine("[ApiGatewayServer] HTTP API läuft auf http://62.68.75.23:8090/");

        _ = Task.Run(MaintainAuthServerConnection);
        _ = Task.Run(MaintainPlayerServerConnection);

        while (_listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleHttpRequest(context));
            }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine("[ApiGatewayServer] HTTP Fehler: " + ex);
            }
        }
    }

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
                        EventLogManager.LogInfo("AuthServer verbunden.");
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

                                EventLogManager.LogInfo($"Empfangen {_authSessions.Count} Sessions vom AuthServer");
                            }
                        }
                        catch (Exception ex)
                        {
                            EventLogManager.LogError("Fehler beim Verarbeiten von Sessions: " + ex.Message);
                        }
                    };

                    _authServerSocket.OnClose += (s, e) =>
                    {
                        EventLogManager.LogInfo("AuthServer Verbindung geschlossen.");
                        _authServerSocket = null;
                    };

                    _authServerSocket.OnError += (s, e) =>
                    {
                        EventLogManager.LogError("AuthServer Fehler: " + e.Message);
                        _authServerSocket = null;
                    };

                    _authServerSocket.Connect();
                }

                if (_authServerSocket?.ReadyState == WebSocketState.Open)
                    _authServerSocket.Send("GET_SESSIONS");
            }
            catch (Exception ex)
            {
                EventLogManager.LogError("Fehler in AuthServer-Wartung: " + ex.Message);
                _authServerSocket = null;
            }

            await Task.Delay(10000);
        }
    }

    private static async Task MaintainPlayerServerConnection()
    {
        while (true)
        {
            try
            {
                if (_playerServerSocket == null || _playerServerSocket.ReadyState != WebSocketState.Open)
                {
                    _playerServerSocket = new WebSocket("ws://62.68.75.23:5002/player");

                    _playerServerSocket.OnOpen += (s, e) =>
                    {
                        EventLogManager.LogInfo("PlayerServer verbunden.");
                        _playerServerSocket.Send("PING");
                    };

                    _playerServerSocket.OnMessage += (s, e) =>
                    {
                        EventLogManager.LogInfo("PlayerServer Nachricht: " + e.Data);

                        if (e.Data.StartsWith("PLAYER_COUNT:"))
                        {
                            var countStr = e.Data.Substring("PLAYER_COUNT:".Length);
                            if (int.TryParse(countStr, out int count))
                                _playerCount = count;
                        }
                    };

                    _playerServerSocket.OnClose += (s, e) =>
                    {
                        EventLogManager.LogInfo("PlayerServer Verbindung geschlossen.");
                        _playerServerSocket = null;
                    };

                    _playerServerSocket.OnError += (s, e) =>
                    {
                        EventLogManager.LogError("PlayerServer Fehler: " + e.Message);
                        _playerServerSocket = null;
                    };

                    _playerServerSocket.Connect();
                }

                if (_playerServerSocket?.ReadyState == WebSocketState.Open)
                    _playerServerSocket.Send("PING");
            }
            catch (Exception ex)
            {
                EventLogManager.LogError("Fehler in PlayerServer-Wartung: " + ex.Message);
                _playerServerSocket = null;
            }

            await Task.Delay(10000);
        }
    }

    private static async Task HandleHttpRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        try
        {
            string json = "";
            int status = 200;

            switch (req.Url?.AbsolutePath)
            {
                case "/sessions":
                    json = JsonSerializer.Serialize(_authSessions.Values);
                    break;

                case "/players/count":
                    json = JsonSerializer.Serialize(new { count = _playerCount });
                    break;

                case "/events/recent":
                case "/logs":
                    var events = EventLogManager.GetRecentEvents();
                    json = JsonSerializer.Serialize(events);
                    break;

                default:
                    status = 404;
                    json = JsonSerializer.Serialize(new { error = "Not Found" });
                    break;
            }

            var buffer = Encoding.UTF8.GetBytes(json);
            res.StatusCode = status;
            res.ContentType = "application/json";
            res.ContentLength64 = buffer.Length;
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ApiGatewayServer] Fehler in HandleHttpRequest: " + ex);
            res.StatusCode = 500;
        }
        finally
        {
            res.Close();
        }
    }
}
