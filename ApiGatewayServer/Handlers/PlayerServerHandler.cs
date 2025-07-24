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
        private static WebSocket? _playerServerSocket;

        // Alle server_info Instanzen speichern (Name als Key)
        private static readonly ConcurrentDictionary<string, ServerInfo> _serverInfos = new();

        // Statusmeldungen der PlayerServer (z.B. ping, uptime, version, etc.)
        private class PlayerStatusInfo
        {
            public DateTime LastUpdated { get; set; }
            public string RawData { get; set; } = "";
            public object? ParsedData { get; set; } = null;
        }

        private static readonly ConcurrentDictionary<string, PlayerStatusInfo> _playerStatusMessages = new();

        private static int _playerCount = 0;

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
                            Log.Information("PlayerServer verbunden.");
                            _playerServerSocket.Send("PING");
                        };

                        _playerServerSocket.OnMessage += (s, e) =>
                        {
                            OnPlayerServerMessage(e.Data);
                        };

                        _playerServerSocket.OnClose += (s, e) =>
                        {
                            Log.Information("PlayerServer Verbindung geschlossen.");
                            _playerServerSocket = null;
                        };

                        _playerServerSocket.OnError += (s, e) =>
                        {
                            Log.Error("PlayerServer Fehler: {Message}", e.Message);
                            _playerServerSocket = null;
                        };

                        _playerServerSocket.Connect();
                    }

                    if (_playerServerSocket?.ReadyState == WebSocketState.Open)
                        _playerServerSocket.Send("PING");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Fehler in PlayerServer-Wartung");
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
                    Log.Information("[{Time}] Player Count aktualisiert: {PlayerCount}", time, _playerCount);
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
                        Log.Debug("[{Time}] server_info raw json: '{MessageData}'", time, messageData);
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
                Log.Error(ex, "[Parsing Error] {MessageType}", messageType);
                parsedData = messageData;
            }

            var statusInfo = new PlayerStatusInfo
            {
                LastUpdated = DateTime.Now,
                RawData = messageData,
                ParsedData = parsedData
            };

            _playerStatusMessages[messageType] = statusInfo;

            Log.Information("[{Time}] Status aktualisiert: {MessageType}", time, messageType);

            PushPlayerStatusUpdate(messageType, statusInfo);
        }

       
    }
}
