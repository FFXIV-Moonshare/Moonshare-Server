using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ApiGateway
{
    public static partial class ApiGatewayServer
    {
        private static readonly ConcurrentDictionary<Guid, System.Net.WebSockets.WebSocket> _webSocketClients = new();

        private static async Task HandleWebSocketClientAsync(HttpListenerContext context)
        {
            System.Net.WebSockets.WebSocket? ws = null;
            var clientId = Guid.NewGuid();

            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                ws = wsContext.WebSocket;

                _webSocketClients.TryAdd(clientId, ws);

                Log.Information("[WS] Client verbunden: {ClientId}", clientId);

                await SendCurrentStatusToClient(ws);

                var buffer = new byte[4096];

                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log.Information("[WS] Client {ClientId} hat die Verbindung geschlossen.", clientId);
                        break;
                    }

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

                                    _serverInfos[info.Name] = info;
                                    _playerStatusMessages["server_info"] = statusInfo;

                                    Log.Information("[WS] server_info empfangen: {Name} mit {PlayersOnline} Spielern.", info.Name, info.PlayersOnline);

                                    PushPlayerStatusUpdate("server_info", statusInfo);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "[WS] Fehler beim Parsen von server_info");
                            }
                        }
                    }
                }

                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WS] Fehler bei Client {ClientId}", clientId);
            }
            finally
            {
                if (ws != null)
                {
                    _webSocketClients.TryRemove(clientId, out _);
                    Log.Information("[WS] Client getrennt: {ClientId}", clientId);
                    ws.Dispose();
                }
            }
        }

        private static async Task SendCurrentStatusToClient(System.Net.WebSockets.WebSocket ws)
        {
            foreach (var kvp in _playerStatusMessages)
            {
                if (ws.State != WebSocketState.Open)
                {
                    Log.Debug("[WS] Verbindung zum Client geschlossen, sende keine Statusupdates mehr.");
                    break;
                }

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
                    Log.Debug("[WS] Statusupdate gesendet: {Type}", kvp.Key);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[WS] Fehler beim Senden eines Statusupdates");
                }
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
                if (client.State == WebSocketState.Open)
                {
                    _ = client.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                Log.Warning(t.Exception, "[WS] Fehler beim Pushen eines Statusupdates");
                        });
                }
            }
        }
    }
}
