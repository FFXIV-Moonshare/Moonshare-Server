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
            "send_message_to/USERID/MESSAGE",
            "list_userids",
            "find_user/USERID",
            "count_files",
            "clear_sessions",
            "server_info",
            "ping",
            "uptime",
            "send_broadcast/MESSAGE",
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
                    "session_count" => SessionManager.Sessions.Count.ToString(),
                    "list_sessions" => ListSessions(),
                    "list_connected" => ListConnectedPlayers(),
                    "kick_inactive" => KickInactive(),
                    "disconnect_all" => DisconnectAll(),
                    "reload_sessions" => ReloadSessions(),
                    //"send_message_to" when parts.Length >= 3 => SendMessageTo(parts[1], parts[2]),  //todo
                    "list_userids" => ListUserIds(),
                    "find_user" when parts.Length >= 2 => FindUser(parts[1]),
                    "count_files" => CountFiles(),
                    "clear_sessions" => ClearSessions(),
                    "server_info" => ServerInfo(),
                    "ping" => "pong",
                    "uptime" => Uptime(),
                    //"send_broadcast" when parts.Length >= 2 => SendBroadcast(parts[1]), //todo
                    "disconnect_user" when parts.Length >= 2 => DisconnectUser(parts[1]),
                    "reload_config" => ReloadConfig(),
                    "toggle_logging" => ToggleLogging(),
                    "list_commands" => ListCommands(),
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
            foreach (var p in SessionManager.ConnectedPlayers.Values)
            {
                p.Context.WebSocket.Close();
                count++;
            }
            Log.Information("KickAll executed: {Count} players kicked", count);
            return $"{count} players kicked.";
        }

        private static string ListOnline()
        {
            var online = SessionManager.GetOnlinePlayers();
            Log.Information("ListOnline executed: {Count} players online", online.Count);
            return JsonSerializer.Serialize(online);
        }

        private static string ListSessions()
        {
            List<AuthSession> sessions = SessionManager.Sessions.Values.ToList();
            Log.Information("ListSessions executed: {Count} sessions returned", sessions.Count);
            return JsonSerializer.Serialize(sessions);
        }

        private static string ListConnectedPlayers()
        {
            var connected = SessionManager.ConnectedPlayers
                .Select(kvp => new { SocketId = kvp.Key, kvp.Value.UserId })
                .ToList();
            Log.Information("ListConnectedPlayers executed: {Count} players connected", connected.Count);
            return JsonSerializer.Serialize(connected);
        }

        private static string KickInactive()
        {
            Log.Warning("KickInactive command called, but not implemented.");
            return "KickInactive not yet implemented.";
        }

        private static string DisconnectAll()
        {
            int count = 0;
            foreach (var player in SessionManager.ConnectedPlayers.Values)
            {
                player.Context.WebSocket.Close();
                count++;
            }
            Log.Information("DisconnectAll executed: {Count} players disconnected", count);
            return "All connected players have been disconnected.";
        }

        private static string ReloadSessions()
        {
            Log.Information("ReloadSessions executed.");
            return "Sessions reloaded (placeholder).";
        }

        private static string ListUserIds()
        {
            var userIds = SessionManager.Sessions.Values.Select(s => s.UserId).Distinct().ToList();
            Log.Information("ListUserIds executed: {Count} unique user IDs", userIds.Count);
            return JsonSerializer.Serialize(userIds);
        }

        private static string FindUser(string userId)
        {
            var session = SessionManager.Sessions.Values.FirstOrDefault(s => s.UserId == userId);
            if (session == null)
            {
                Log.Information("FindUser: User {UserId} not found", userId);
                return $"User {userId} not found.";
            }
            Log.Information("FindUser: User {UserId} found", userId);
            return JsonSerializer.Serialize(session);
        }

        private static string CountFiles()
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "ReceivedFiles");
            if (!Directory.Exists(folder))
            {
                Log.Information("CountFiles executed: ReceivedFiles folder does not exist.");
                return "No files received yet.";
            }
            int count = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length;
            Log.Information("CountFiles executed: {Count} files counted", count);
            return count.ToString();
        }

        private static string ClearSessions()
        {
            int count = SessionManager.Sessions.Count;
            SessionManager.Sessions.Clear();
            Log.Warning("ClearSessions executed: {Count} sessions cleared", count);
            return $"Cleared {count} sessions.";
        }

        private static string ServerInfo()
        {
            var uptime = (DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
            var info = new
            {
                ServerTime = DateTime.UtcNow,
                UptimeSeconds = (int)uptime,
                ConnectedPlayers = SessionManager.ConnectedPlayers.Count,
                TotalSessions = SessionManager.Sessions.Count
            };
            Log.Information("ServerInfo requested");
            return JsonSerializer.Serialize(info);
        }

        private static string Uptime()
        {
            var uptime = (DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime());
            Log.Information("Uptime requested: {Uptime}", uptime);
            return uptime.ToString(@"dd\.hh\:mm\:ss");
        }

        private static string DisconnectUser(string userId)
        {
            var player = SessionManager.ConnectedPlayers.Values.FirstOrDefault(p => p.UserId == userId);
            if (player == null)
            {
                Log.Warning("DisconnectUser: User {UserId} not found", userId);
                return $"User {userId} not found.";
            }
            player.Context.WebSocket.Close();
            Log.Information("DisconnectUser: User {UserId} disconnected", userId);
            return $"User {userId} disconnected.";
        }

        private static string ReloadConfig()
        {
            Log.Information("ReloadConfig called - no config system implemented.");
            return "ReloadConfig not implemented.";
        }

        private static string ToggleLogging()
        {
            Log.Information("ToggleLogging called - no toggle implemented.");
            return "ToggleLogging not implemented.";
        }

        private static string ListCommands()
        {
            Log.Information("ListCommands executed.");
            return string.Join(", ", Commands);
        }

        private static string DebugSessions()
        {
            var debugInfo = SessionManager.Sessions.Select(kvp =>
                new
                {
                    Token = kvp.Key,
                    UserId = kvp.Value.UserId,
                    CreatedAt = kvp.Value.CreatedAt,
                }).ToList();
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
            SessionManager.Sessions[token] = session;
            Log.Information("AddMockUser: Added mock user {UserId} with token {Token}", userId, token);
            return $"Mock user {userId} added with token {token}.";
        }

        private static string RemoveMockUsers()
        {
            var mockTokens = SessionManager.Sessions.Where(kvp => Guid.TryParse(kvp.Key, out _))
                .Select(kvp => kvp.Key).ToList();
            foreach (var token in mockTokens)
                SessionManager.Sessions.TryRemove(token, out _);
            Log.Information("RemoveMockUsers executed, removed {Count} mock sessions", mockTokens.Count);
            return $"Removed {mockTokens.Count} mock users.";
        }

        private static string ExportSessions()
        {
            var sessions = SessionManager.Sessions.Values.ToList();
            var json = JsonSerializer.Serialize(sessions);
            Log.Information("ExportSessions executed, exporting {Count} sessions", sessions.Count);
            return json;
        }

        private static string StatsUsersPerShard()
        {
            var shardCounts = SessionManager.Sessions.Values
                .GroupBy(s => (GetStableHash(s.SessionToken) % 3))
                .Select(g => new { Shard = g.Key, Count = g.Count() })
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
