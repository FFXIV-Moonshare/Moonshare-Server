using Moonshare.Server.Managers;
using Moonshare.Server.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Moonshare.Server.WebSocketHandlers
{
    public static class AdminCommandHandler
    {
        private static readonly string[] Commands = new[]
        {
            "kickall",
            "list_online",
            "session_count",
            "list_sessions",
            "list_connected",
            "kick_inactive",
            "disconnect_all",
            "reload_sessions",
            //"send_message_to/USERID/MESSAGE",
            "list_userids",
            "find_user/USERID",
            "count_files",
            "clear_sessions",
            "server_info",
            "ping",
            "uptime",
            //"send_broadcast/MESSAGE",
            "disconnect_user/USERID",
            "reload_config",
            "toggle_logging",
            "list_commands",
            "debug_sessions",
            "add_mock_user/USERID",
            "remove_mock_users",
            "export_sessions",
            "import_sessions",
            "stats_users_per_shard",
            "gc_collect",
            "version"
        };

        private const int ShardCount = 3; // Muss zum System passen!

        public static void LogAvailableCommandsOnStartup()
        {
            Log.Information("Available admin commands: {Commands}", string.Join(", ", Commands));
        }

        public static string Handle(string command)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(command))
                    return "No command provided.";

                var parts = command.Split('/', 3, StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLowerInvariant();

                Log.Information("Received admin command: {Command}", command);

                return cmd switch
                {
                    "kickall" => KickAll(),
                    "list_online" => ListOnline(),
                    "session_count" => SessionCount(),
                    "list_sessions" => ListSessions(),
                    "list_connected" => ListConnectedPlayers(),

                    //"send_message_to" when parts.Length >= 3 => SendMessageTo(parts[1], parts[2]),
                    "list_userids" => ListUserIds(),
              
                    "clear_sessions" => ClearSessions(),
                    "server_info" => ServerInfo(),
                    "ping" => "pong",
              
                    //"send_broadcast" when parts.Length >= 2 => SendBroadcast(parts[1]),
                    "disconnect_user" when parts.Length >= 2 => DisconnectUser(parts[1]),

                    "debug_sessions" => DebugSessions(),
                    "add_mock_user" when parts.Length >= 2 => AddMockUser(parts[1]),
                    "remove_mock_users" => RemoveMockUsers(),
                    "export_sessions" => ExportSessions(),
                    "import_sessions" => "Import sessions is not implemented via this interface.",
                    "stats_users_per_shard" => StatsUsersPerShard(),
                    "gc_collect" => GcCollect(),
                    "version" => Version(),
                    _ => "Unknown command"
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling admin command: {Command}", command);
                return $"Error handling command: {ex.Message}";
            }
        }

        private static string KickAll()
        {
            int count = 0;
            foreach (var shardDict in SessionManager.ActivePlayers.Values)
            {
                foreach (var player in shardDict.Values)
                {
                    player.Context.WebSocket.Close();
                    count++;
                }
            }
            Log.Information("KickAll executed: {Count} players kicked", count);
            return $"{count} players kicked.";
        }

        private static string ListOnline()
        {
            var allUsers = SessionManager.ActivePlayers.Values
                .SelectMany(dict => dict.Values.Select(p => p.UserId))
                .Distinct()
                .ToList();

            Log.Information("ListOnline executed: {Count} players online", allUsers.Count);
            return JsonSerializer.Serialize(allUsers);
        }

        private static string SessionCount()
        {
            int totalSessions = SessionManager.ActiveSessions.Values.Sum(dict => dict.Count);
            return totalSessions.ToString();
        }

        private static string ListSessions()
        {
            var sessions = SessionManager.ActiveSessions.Values.SelectMany(dict => dict.Values).ToList();
            Log.Information("ListSessions executed: {Count} sessions returned", sessions.Count);
            return JsonSerializer.Serialize(sessions);
        }

        private static string ListConnectedPlayers()
        {
            var connected = SessionManager.ActivePlayers.Values
                .SelectMany(dict => dict.Select(kvp => new { SocketId = kvp.Key, kvp.Value.UserId }))
                .ToList();
            Log.Information("ListConnectedPlayers executed: {Count} players connected", connected.Count);
            return JsonSerializer.Serialize(connected);
        }

        private static string ListUserIds()
        {
            var userIds = SessionManager.ActiveSessions.Values
                .SelectMany(dict => dict.Values)
                .Select(s => s.UserId)
                .Distinct()
                .ToList();

            Log.Information("ListUserIds executed: {Count} unique user IDs", userIds.Count);
            return JsonSerializer.Serialize(userIds);
        }

        private static string FindUser(string userId)
        {
            var session = SessionManager.ActiveSessions.Values
                .SelectMany(dict => dict.Values)
                .FirstOrDefault(s => s.UserId == userId);

            if (session == null)
            {
                Log.Information("FindUser: User {UserId} not found", userId);
                return $"User {userId} not found.";
            }
            Log.Information("FindUser: User {UserId} found", userId);
            return JsonSerializer.Serialize(session);
        }

        private static string ClearSessions()
        {
            int count = SessionManager.ActiveSessions.Values.Sum(dict => dict.Count);
            foreach (var dict in SessionManager.ActiveSessions.Values)
                dict.Clear();

            Log.Warning("ClearSessions executed: {Count} sessions cleared", count);
            return $"Cleared {count} sessions.";
        }

        private static string ServerInfo()
        {
            var uptime = (DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
            int connectedPlayers = SessionManager.ActivePlayers.Values.Sum(dict => dict.Count);
            int totalSessions = SessionManager.ActiveSessions.Values.Sum(dict => dict.Count);

            var info = new
            {
                ServerTime = DateTime.UtcNow,
                UptimeSeconds = (int)uptime,
                ConnectedPlayers = connectedPlayers,
                TotalSessions = totalSessions
            };
            Log.Information("ServerInfo requested");
            return JsonSerializer.Serialize(info);
        }

        private static string DisconnectUser(string userId)
        {
            foreach (var shardDict in SessionManager.ActivePlayers.Values)
            {
                var player = shardDict.Values.FirstOrDefault(p => p.UserId == userId);
                if (player != null)
                {
                    player.Context.WebSocket.Close();
                    Log.Information("DisconnectUser: User {UserId} disconnected", userId);
                    return $"User {userId} disconnected.";
                }
            }

            Log.Warning("DisconnectUser: User {UserId} not found", userId);
            return $"User {userId} not found.";
        }

        private static string DebugSessions()
        {
            var debugInfo = SessionManager.ActiveSessions.Values
                .SelectMany(dict => dict.Select(kvp =>
                    new
                    {
                        Token = kvp.Key,
                        UserId = kvp.Value.UserId,
                        CreatedAt = kvp.Value.CreatedAt,
                    }))
                .ToList();

            Log.Information("DebugSessions executed, {Count} sessions", debugInfo.Count);
            return JsonSerializer.Serialize(debugInfo);
        }

        private static string AddMockUser(string userId)
        {
            var token = Guid.NewGuid().ToString();
            var session = new AuthSession
            {
                UserId = userId,
                SessionToken = token,
                CreatedAt = DateTime.UtcNow
            };
            int shardId = GetStableHash(token) % ShardCount;
            var shardSessions = SessionManager.ActiveSessions.GetOrAdd(shardId, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, AuthSession>());
            shardSessions[token] = session;

            Log.Information("AddMockUser: Added mock user {UserId} with token {Token} in shard {ShardId}", userId, token, shardId);
            return $"Mock user {userId} added with token {token}.";
        }

        private static string RemoveMockUsers()
        {
            int removedCount = 0;
            foreach (var dict in SessionManager.ActiveSessions.Values)
            {
                var mockTokens = dict.Where(kvp => Guid.TryParse(kvp.Key, out _)).Select(kvp => kvp.Key).ToList();
                foreach (var token in mockTokens)
                {
                    if (dict.TryRemove(token, out _))
                        removedCount++;
                }
            }
            Log.Information("RemoveMockUsers executed, removed {Count} mock sessions", removedCount);
            return $"Removed {removedCount} mock users.";
        }

        private static string ExportSessions()
        {
            var sessions = SessionManager.ActiveSessions.Values.SelectMany(dict => dict.Values).ToList();
            var json = JsonSerializer.Serialize(sessions);
            Log.Information("ExportSessions executed, exporting {Count} sessions", sessions.Count);
            return json;
        }

        private static string StatsUsersPerShard()
        {
            var shardCounts = SessionManager.ActiveSessions
                .Select(kvp => new { Shard = kvp.Key, Count = kvp.Value.Count })
                .ToList();
            Log.Information("StatsUsersPerShard executed");
            return JsonSerializer.Serialize(shardCounts);
        }

        private static int GetStableHash(string str)
        {
            unchecked
            {
                int hash = 23;
                foreach (var c in str)
                    hash = hash * 31 + c;
                return Math.Abs(hash);
            }
        }

        private static string GcCollect()
        {
            GC.Collect();
            Log.Information("GC.Collect() called manually.");
            return "Garbage Collector invoked.";
        }

        private static string Version()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            Log.Information("Version requested: {Version}", version);
            return $"Server version: {version}";
        }
    }
}
