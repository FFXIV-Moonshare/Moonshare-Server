using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Moonshare.Server.Models;
using Moonshare.Server.WebSocketHandlers;
using Serilog;

namespace Moonshare.Server.Managers
{
    public static class SessionManager
    {
     
        public static ConcurrentDictionary<string, PlayerBehavior> ConnectedPlayers { get; } = new();

       
        public static ConcurrentDictionary<string, AuthSession> Sessions { get; } = new();

        
        public static bool ValidateSession(string token, out AuthSession? session)
        {
            bool result = Sessions.TryGetValue(token, out session);
            if (result)
            {
                Log.Information("Session validated for token {Token}, UserId {UserId}", token, session?.UserId);
            }
            else
            {
                Log.Warning("Invalid session token validation attempt: {Token}", token);
            }
            return result;
        }

    
        public static List<string> GetOnlinePlayers()
        {
            var online = ConnectedPlayers.Values.Select(p => p.UserId).ToList();
            Log.Information("Retrieved online players list: {Count} players online", online.Count);
            return online;
        }

      
        public static void AddConnectedPlayer(string connectionId, PlayerBehavior player)
        {
            if (ConnectedPlayers.TryAdd(connectionId, player))
            {
                Log.Information("Player connected: UserId {UserId}, ConnectionId {ConnectionId}", player.UserId, connectionId);
            }
            else
            {
                Log.Warning("Failed to add connected player: ConnectionId {ConnectionId}", connectionId);
            }
        }

       
        public static void RemoveConnectedPlayer(string connectionId)
        {
            if (ConnectedPlayers.TryRemove(connectionId, out var player))
            {
                Log.Information("Player disconnected: UserId {UserId}, ConnectionId {ConnectionId}", player.UserId, connectionId);
            }
            else
            {
                Log.Warning("Failed to remove connected player: ConnectionId {ConnectionId}", connectionId);
            }
        }
    }
}
