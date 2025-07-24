using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace ApiGateway
{
    public static partial class ApiGatewayServer
    {
        private static async Task HandleHttpRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            try
            {
                string json = "";
                int status = 200;

                string path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "";

                Log.Information("HTTP Request: {Method} {Url} from {RemoteEndPoint}", req.HttpMethod, req.Url, req.RemoteEndPoint);

                switch (path)
                {
                    case "/sessions":
                        json = JsonSerializer.Serialize(_authSessions.Values);
                        break;

                    case "/players/count":
                        json = JsonSerializer.Serialize(new { count = _playerCount });
                        break;

                    case "/server_info":
                        {
                            int totalPlayersOnline = 0;
                            int totalMaxPlayers = 0;
                            var serversList = new List<object>();

                            foreach (var si in _serverInfos.Values)
                            {
                                totalPlayersOnline += si.PlayersOnline;
                                totalMaxPlayers += si.MaxPlayers;

                                serversList.Add(new
                                {
                                    name = si.Name,
                                    players_online = si.PlayersOnline,
                                    max_players = si.MaxPlayers,
                                    server_version = si.ServerVersion,
                                    uptime_seconds = si.UptimeSeconds,
                                    status = si.PlayersOnline > 0 ? "on" : "off"
                                });
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

                    case "/ping":
                        {
                            if (_playerStatusMessages.TryGetValue("ping", out var info))
                            {
                                if (int.TryParse(info.ParsedData?.ToString(), out int latencyMs))
                                {
                                    json = JsonSerializer.Serialize(new { latency_ms = latencyMs });
                                }
                                else
                                {
                                    json = JsonSerializer.Serialize(new { ping_raw = info.RawData });
                                }
                            }
                            else
                            {
                                json = JsonSerializer.Serialize(new { error = "No ping data available" });
                                status = 404;
                                Log.Warning("Keine Ping-Daten verfügbar für Request {Url}", req.Url);
                            }
                        }
                        break;

                    case "/uptime":
                        {
                            if (_playerStatusMessages.TryGetValue("uptime", out var info))
                            {
                                if (info.ParsedData is TimeSpan uptimeSpan)
                                {
                                    json = JsonSerializer.Serialize(new { uptime = uptimeSpan.ToString(@"dd\.hh\:mm\:ss") });
                                }
                                else
                                {
                                    json = JsonSerializer.Serialize(new { uptime_raw = info.RawData });
                                }
                            }
                            else
                            {
                                json = JsonSerializer.Serialize(new { error = "No uptime data available" });
                                status = 404;
                                Log.Warning("Keine Uptime-Daten verfügbar für Request {Url}", req.Url);
                            }
                        }
                        break;

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
                                status = 404;
                                Log.Warning("Keine Versions-Daten verfügbar für Request {Url}", req.Url);
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
                        Log.Warning("Ungültiger Pfad angefragt: {Url}", req.Url);
                        break;
                }

                var buffer = Encoding.UTF8.GetBytes(json);
                res.StatusCode = status;
                res.ContentType = "application/json";
                res.ContentLength64 = buffer.Length;
                await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);

                Log.Information("HTTP Response: {StatusCode} für {Url}", status, req.Url);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fehler in HandleHttpRequest");
                res.StatusCode = 500;
            }
            finally
            {
                res.Close();
            }
        }
    }
}
