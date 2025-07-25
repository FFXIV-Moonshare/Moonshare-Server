using Moonshare.Server.Models;
using Moonshare.Server.WebSocketHandlers;
using PlayerServer.Config;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Moonshare.Server.Managers
{
    public static class SessionManager
    {
        // Konfigurationsobjekt
        public static ServerConfig? Config { get; set; }

        // Entfernungsdelay unverändert
        private static readonly TimeSpan RemovalDelay = TimeSpan.FromMinutes(10);

        public static int MaxActiveSessionsPerShard => Config?.MaxSessions ?? 10000;
        public static int MaxQueuedSessionsPerShard => Config?.MaxSessions ?? 10000;

        public static int MaxActivePlayersPerShard => Config?.PlayerLimit ?? 10000;
        public static int MaxQueuedPlayersPerShard => Config?.PlayerLimit ?? 10000;

        // Aktive Sessions pro Shard
        public static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, AuthSession>> ActiveSessions = new();

        // Warteschlangen für Sessions pro Shard
        private static readonly ConcurrentDictionary<int, ConcurrentQueue<(string token, AuthSession session)>> QueuedSessions = new();

        // Aktive Spieler pro Shard
        public static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, PlayerBehavior>> ActivePlayers = new();

        // Warteschlangen für Spieler pro Shard
        private static readonly ConcurrentDictionary<int, ConcurrentQueue<PlayerBehavior>> QueuedPlayers = new();

        // Pending-Removals für Sessions und Spieler (für verzögerte Entfernung)
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, DateTime>> PendingSessionRemovals = new();
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, DateTime>> PendingPlayerRemovals = new();

        // Timer zur Bereinigung
        private static readonly Timer CleanupTimer;

        static SessionManager()
        {
            CleanupTimer = new Timer(CleanupPendingRemovals, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }



        private static void CleanupPendingRemovals(object? state)
        {
            DateTime now = DateTime.UtcNow;

            // Spieler Pending Removal
            foreach (var kvp in PendingPlayerRemovals)
            {
                int shardId = kvp.Key;
                var removals = kvp.Value;

                if (!ActivePlayers.TryGetValue(shardId, out var players)) continue;

                var toRemove = removals.Where(pair => (now - pair.Value) >= RemovalDelay).Select(pair => pair.Key).ToList();

                foreach (var connectionId in toRemove)
                {
                    if (players.TryRemove(connectionId, out var player))
                    {
                        Log.Information("Shard {ShardId}: Player finally removed after delay: UserId {UserId}, ConnectionId {ConnectionId}", shardId, player.UserId, connectionId);
                    }
                    removals.TryRemove(connectionId, out _);
                }

                TryProcessQueuedPlayers(shardId);
            }

            // Sessions Pending Removal
            foreach (var kvp in PendingSessionRemovals)
            {
                int shardId = kvp.Key;
                var removals = kvp.Value;

                if (!ActiveSessions.TryGetValue(shardId, out var sessions)) continue;

                var toRemove = removals.Where(pair => (now - pair.Value) >= RemovalDelay).Select(pair => pair.Key).ToList();

                foreach (var token in toRemove)
                {
                    if (sessions.TryRemove(token, out var session))
                    {
                        Log.Information("Shard {ShardId}: Session finally removed after delay: Token {Token}, UserId {UserId}", shardId, token, session.UserId);
                    }
                    removals.TryRemove(token, out _);
                }

                TryProcessQueuedSessions(shardId);
            }
        }
        private static void TryProcessQueuedPlayers(int shardId)
        {
            var players = ActivePlayers.GetOrAdd(shardId, _ => new ConcurrentDictionary<string, PlayerBehavior>());
            var queue = QueuedPlayers.GetOrAdd(shardId, _ => new ConcurrentQueue<PlayerBehavior>());

            while (players.Count < MaxActivePlayersPerShard && queue.TryDequeue(out var nextPlayer))
            {
                var connectionId = nextPlayer.ID;
                if (players.TryAdd(connectionId, nextPlayer))
                {
                    Log.Information("Shard {ShardId}: Player {UserId} connected from queue (QueueLength={QueueLength})", shardId, nextPlayer.UserId, queue.Count);
                }
                else
                {
                    Log.Warning("Shard {ShardId}: Failed to connect player from queue: UserId {UserId}, re-queueing", shardId, nextPlayer.UserId);
                    queue.Enqueue(nextPlayer);
                    break;
                }
            }
        }

        private static async void TryProcessQueuedSessions(int shardId)
        {
            var sessions = ActiveSessions.GetOrAdd(shardId, _ => new ConcurrentDictionary<string, AuthSession>());
            var queue = QueuedSessions.GetOrAdd(shardId, _ => new ConcurrentQueue<(string token, AuthSession session)>());

            while (sessions.Count < MaxActiveSessionsPerShard && queue.TryDequeue(out var next))
            {
                int baseDelayMs = 500;
                int dynamicDelayMs = (int)(sessions.Count / 2.0);
                int totalDelay = baseDelayMs + dynamicDelayMs;
                await Task.Delay(totalDelay);

                if (sessions.TryAdd(next.token, next.session))
                {
                    Log.Information("Shard {ShardId}: Session für UserId {UserId} aus Warteschlange verbunden (Delay={Delay}ms)", shardId, next.session.UserId, totalDelay);
                }
                else
                {
                    Log.Warning("Shard {ShardId}: Session konnte nicht hinzugefügt werden, wird erneut in Warteschlange gestellt: UserId {UserId}", shardId, next.session.UserId);
                    queue.Enqueue(next);
                    break;
                }
            }
        }

        public static bool AddConnectedPlayer(int shardId, string connectionId, PlayerBehavior player)
        {
            var players = ActivePlayers.GetOrAdd(shardId, _ => new ConcurrentDictionary<string, PlayerBehavior>());
            var queue = QueuedPlayers.GetOrAdd(shardId, _ => new ConcurrentQueue<PlayerBehavior>());

            if (players.Count >= MaxActivePlayersPerShard)
            {
                if (queue.Count >= MaxQueuedPlayersPerShard)
                {
                    Log.Warning("Shard {ShardId}: Warteschlange für Spieler voll (max {MaxQueuedPlayersPerShard}). Player {UserId} abgelehnt.", shardId, MaxQueuedPlayersPerShard, player.UserId);
                    return false;
                }

                queue.Enqueue(player);
                Log.Information("Shard {ShardId}: MaxPlayers erreicht, Player {UserId} in Warteschlange (QueueLength={QueueLength})", shardId, player.UserId, queue.Count);
                return false;
            }

            if (players.TryAdd(connectionId, player))
            {
                Log.Information("Shard {ShardId}: Player verbunden: UserId {UserId}, ConnectionId {ConnectionId}", shardId, player.UserId, connectionId);
                return true;
            }

            Log.Warning("Shard {ShardId}: Spieler konnte nicht hinzugefügt werden: ConnectionId {ConnectionId}", shardId, connectionId);
            return false;
        }

        public static void RemoveConnectedPlayer(int shardId, string connectionId)
        {
            var removals = PendingPlayerRemovals.GetOrAdd(shardId, _ => new ConcurrentDictionary<string, DateTime>());

            if (!removals.ContainsKey(connectionId))
            {
                removals[connectionId] = DateTime.UtcNow;
                Log.Information("Shard {ShardId}: Player marked for delayed removal: ConnectionId {ConnectionId}", shardId, connectionId);
            }
        }


           public static bool AddSession(int shardId, string token, AuthSession session)
        {
            var queue = QueuedSessions.GetOrAdd(shardId, _ => new ConcurrentQueue<(string, AuthSession)>());

            if (queue.Count >= MaxQueuedSessionsPerShard)
            {
                Log.Warning("Shard {ShardId}: Warteschlange für Sessions voll. Session für UserId {UserId} abgelehnt.", shardId, session.UserId);
                return false;
            }

            queue.Enqueue((token, session));
            Log.Information("Shard {ShardId}: Session für UserId {UserId} zur Warteschlange hinzugefügt (QueueLength={QueueLength})", shardId, session.UserId, queue.Count);

            _ = Task.Run(() => TryProcessQueuedSessions(shardId));
            return true;
        }

        public static void RemoveSession(int shardId, string token)
        {
            var removals = PendingSessionRemovals.GetOrAdd(shardId, _ => new ConcurrentDictionary<string, DateTime>());

            if (!removals.ContainsKey(token))
            {
                removals[token] = DateTime.UtcNow;
                Log.Information("Shard {ShardId}: Session marked for delayed removal: Token {Token}", shardId, token);
            }
        }


        public static bool ValidateSession(string token, out AuthSession? session)
        {
            session = null;
            foreach (var shardSessions in ActiveSessions.Values)
            {
                if (shardSessions.TryGetValue(token, out session))
                {
                    Log.Information("Session validated for token {Token}, UserId {UserId}", token, session?.UserId);
                    return true;
                }
            }
            Log.Warning("Invalid session token validation attempt: {Token}", token);
            return false;
        }

        public static List<string> GetOnlinePlayers(int shardId)
        {
            if (ActivePlayers.TryGetValue(shardId, out var players))
            {
                var online = players.Values.Select(p => p.UserId).ToList();
                Log.Information("Shard {ShardId}: Retrieved online players list: {Count} players online", shardId, online.Count);
                return online;
            }
            return new List<string>();
        }

        public static List<string> AddDummySessions(int totalDummySessionsGlobal, int shardId, int totalShards)
        {
            var addedTokens = new List<string>();

            // Berechne, wie viele Dummy-Sessions dieser Shard bekommen soll (fair verteilt)
            int sessionsPerShard = totalDummySessionsGlobal / totalShards;
            if (shardId == totalShards - 1) // Letzter Shard bekommt evtl. Rest
                sessionsPerShard += totalDummySessionsGlobal % totalShards;

            for (int i = 0; i < sessionsPerShard; i++)
            {
                // Dummy Token und UserId erzeugen (UUIDs oder beliebige Strings)
                string token = Guid.NewGuid().ToString();
                string userId = $"dummy_user_{shardId}_{i}";

                var session = new AuthSession
                {
                    SessionToken = token,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                bool added = AddSession(shardId, token, session);
                if (added)
                {
                    addedTokens.Add(token);
                }
                else
                {
                    Log.Warning("Failed to add dummy session for UserId {UserId} in Shard {ShardId}", userId, shardId);
                }
            }

            Log.Information("Shard {ShardId}: Added {Count} dummy sessions.", shardId, addedTokens.Count);
            return addedTokens;
        }

        // Optional: Methoden, um Warteschlangen-Größen oder aktive Counts abzufragen:

        public static int GetActivePlayerCount(int shardId) =>
            ActivePlayers.TryGetValue(shardId, out var players) ? players.Count : 0;

        public static int GetQueuedPlayerCount(int shardId) =>
            QueuedPlayers.TryGetValue(shardId, out var queue) ? queue.Count : 0;

        public static int GetActiveSessionCount(int shardId) =>
            ActiveSessions.TryGetValue(shardId, out var sessions) ? sessions.Count : 0;

        public static int GetQueuedSessionCount(int shardId) =>
            QueuedSessions.TryGetValue(shardId, out var queue) ? queue.Count : 0;
    }
}
