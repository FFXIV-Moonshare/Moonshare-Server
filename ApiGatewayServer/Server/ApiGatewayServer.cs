using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using WebSocketSharp;
using WebSocket = WebSocketSharp.WebSocket;
using WebSocketState = WebSocketSharp.WebSocketState;

public static class ApiGatewayServer
{
    private static HttpListener? _listener;

    private static ConcurrentDictionary<string, AuthSession> _authSessions = new();

    private static int _playerCount = 0;

    private static WebSocket? _authServerSocket;
    private static WebSocket? _playerServerSocket;

    private static readonly ConcurrentDictionary<Guid, System.Net.WebSockets.WebSocket> _webSocketClients = new();

    // NEU: Alle server_info Instanzen speichern (Name als Key)
    private static readonly ConcurrentDictionary<string, ServerInfo> _serverInfos = new();

    private class PlayerStatusInfo
    {
        public DateTime LastUpdated { get; set; }
        public string RawData { get; set; } = "";
        public object? ParsedData { get; set; } = null;
    }

    private static readonly ConcurrentDictionary<string, PlayerStatusInfo> _playerStatusMessages = new();

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

                if (context.Request.IsWebSocketRequest && context.Request.Url?.AbsolutePath == "/ws")
                {
                    _ = Task.Run(() => HandleWebSocketClientAsync(context));
                }
                else
                {
                    _ = Task.Run(() => HandleHttpRequest(context));
                }
            }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine("[ApiGatewayServer] HTTP Fehler: " + ex);
            }
        }
    }

    #region AuthServer Connection

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

    #endregion

    #region PlayerServer Connection und Message Handling

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
                        OnPlayerServerMessage(e.Data);
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

    private static void OnPlayerServerMessage(string message)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        string data = message.Trim();

        if (data.StartsWith("PLAYER_COUNT:"))
        {
            var countStr = data.Substring("PLAYER_COUNT:".Length);
            if (int.TryParse(countStr, out int count))
            {
                _playerCount = count;
                Console.WriteLine($"[{time} INF] Player Count aktualisiert: {_playerCount}");
            }
            return;
        }

        var split = data.Split(new char[] { ':' }, 2);
        string messageType = split[0].Trim().ToLowerInvariant();
        string messageData = split.Length > 1 ? split[1] : "";

        object? parsedData = null;

        try
        {
            switch (messageType)
            {
                case "server_info":
                    Console.WriteLine($"[DEBUG] server_info raw json: '{messageData}'");
                    var info = JsonSerializer.Deserialize<ServerInfo>(messageData);
                    if (info != null && !string.IsNullOrEmpty(info.Name))
                    {
                        _serverInfos[info.Name] = info;
                        parsedData = info;
                    }
                    else
                    {
                        parsedData = messageData;
                    }
                    break;

                case "ping":
                    parsedData = messageData;
                    break;

                case "uptime":
                    if (int.TryParse(messageData, out int uptimeSeconds))
                        parsedData = TimeSpan.FromSeconds(uptimeSeconds);
                    else
                        parsedData = messageData;
                    break;

                case "version":
                    parsedData = messageData;
                    break;

                default:
                    parsedData = messageData;
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Parsing Error] {messageType}: {ex.Message}");
            parsedData = messageData;
        }

        var statusInfo = new PlayerStatusInfo
        {
            LastUpdated = DateTime.Now,
            RawData = messageData,
            ParsedData = parsedData
        };

        _playerStatusMessages[messageType] = statusInfo;

        Console.WriteLine($"[{time} INF] Status aktualisiert: {messageType}");

        PushPlayerStatusUpdate(messageType, statusInfo);
    }

    private class ServerInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("players_online")]
        public int PlayersOnline { get; set; }

        [JsonPropertyName("max_players")]
        public int MaxPlayers { get; set; }

        [JsonPropertyName("server_version")]
        public string? ServerVersion { get; set; }

        [JsonPropertyName("uptime_seconds")]
        public int UptimeSeconds { get; set; }
    }

    #endregion

    #region WebSocket-Clients verwalten und Push

    private static async Task HandleWebSocketClientAsync(HttpListenerContext context)
    {
        System.Net.WebSockets.WebSocket? ws = null;
        var clientId = Guid.NewGuid();

        try
        {
            var wsContext = await context.AcceptWebSocketAsync(null);
            ws = wsContext.WebSocket;

            _webSocketClients.TryAdd(clientId, ws);

            Console.WriteLine($"[WS] Client verbunden: {clientId}");

            await SendCurrentStatusToClient(ws);

            var buffer = new byte[4096];

            while (ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count).Trim();

                    if (message.StartsWith("server_info:", StringComparison.OrdinalIgnoreCase))
                    {
                        var jsonData = message.Substring("server_info:".Length);

                        try
                        {
                            var info = JsonSerializer.Deserialize<ServerInfo>(jsonData);
                            if (info != null && !string.IsNullOrEmpty(info.Name))
                            {
                                var statusInfo = new PlayerStatusInfo
                                {
                                    LastUpdated = DateTime.Now,
                                    RawData = jsonData,
                                    ParsedData = info
                                };

                                // auch hier multi-shard speichern
                                _serverInfos[info.Name] = info;

                                // ebenfalls generisch abspeichern
                                _playerStatusMessages["server_info"] = statusInfo;

                                Console.WriteLine($"[WS] server_info empfangen: {info.Name} mit {info.PlayersOnline} Spielern.");
                                PushPlayerStatusUpdate("server_info", statusInfo);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WS] Fehler beim Parsen von server_info: {ex.Message}");
                        }
                    }
                }
            }

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WS] Fehler: {ex}");
        }
        finally
        {
            if (ws != null)
            {
                _webSocketClients.TryRemove(clientId, out _);
                Console.WriteLine($"[WS] Client getrennt: {clientId}");
                ws.Dispose();
            }
        }
    }

    private static async Task SendCurrentStatusToClient(System.Net.WebSockets.WebSocket ws)
    {
        foreach (var kvp in _playerStatusMessages)
        {
            if (ws.State != System.Net.WebSockets.WebSocketState.Open)
                break;

            var payload = new
            {
                type = kvp.Key,
                timestamp = kvp.Value.LastUpdated.ToString("o"),
                data = kvp.Value.ParsedData ?? kvp.Value.RawData
            };

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);

            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }
    }

    private static void PushPlayerStatusUpdate(string messageType, PlayerStatusInfo info)
    {
        var payload = new
        {
            type = messageType,
            timestamp = info.LastUpdated.ToString("o"),
            data = info.ParsedData ?? info.RawData
        };

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        var buffer = new ArraySegment<byte>(bytes);

        foreach (var client in _webSocketClients.Values)
        {
            if (client.State == System.Net.WebSockets.WebSocketState.Open)
            {
                _ = client.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None)
                    .ContinueWith(t => { });
            }
        }
    }

    #endregion

    #region HTTP API Handler

    private static async Task HandleHttpRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        try
        {
            string json = "";
            int status = 200;

            string path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";

            switch (path)
            {
                case "/sessions":
                    json = JsonSerializer.Serialize(_authSessions.Values);
                    break;

                case "/players/count":
                    json = JsonSerializer.Serialize(new { count = _playerCount });
                    break;

                // Aggregierte server_info Ausgabe für Multi-Shard:
                case "/server_info":
                    {
                        int totalPlayersOnline = 0;
                        int totalMaxPlayers = 0;
                        var serversList = new List<ServerInfo>();

                        foreach (var si in _serverInfos.Values)
                        {
                            totalPlayersOnline += si.PlayersOnline;
                            totalMaxPlayers += si.MaxPlayers;
                            serversList.Add(si);
                        }

                        var combined = new
                        {
                            total_players_online = totalPlayersOnline,
                            total_max_players = totalMaxPlayers,
                            servers = serversList
                        };

                        json = JsonSerializer.Serialize(combined);
                    }
                    break;

                // Einfacher Zugriff auf einzelne Statuswerte
                case "/ping":
                case "/uptime":
                case "/version":
                    {
                        var key = path.Trim('/');
                        if (_playerStatusMessages.TryGetValue(key, out var info))
                        {
                            json = JsonSerializer.Serialize(new
                            {
                                lastUpdated = info.LastUpdated.ToString("o"),
                                rawData = info.RawData,
                                parsedData = info.ParsedData
                            });
                        }
                        else
                        {
                            json = JsonSerializer.Serialize(new { error = $"No {key} data available" });
                        }
                    }
                    break;

                case "/player/status":
                    {
                        var statusDict = new Dictionary<string, object>();
                        foreach (var kvp in _playerStatusMessages)
                        {
                            statusDict[kvp.Key] = new
                            {
                                lastUpdated = kvp.Value.LastUpdated.ToString("o"),
                                rawData = kvp.Value.RawData,
                                parsedData = kvp.Value.ParsedData
                            };
                        }
                        json = JsonSerializer.Serialize(statusDict);
                    }
                    break;

                case "/events/recent":
                case "/logs":
                    {
                        var events = EventLogManager.GetRecentEvents();
                        json = JsonSerializer.Serialize(events);
                    }
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

    #endregion
}
