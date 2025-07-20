using Moonshare.Server.Models;

using System.Collections.Concurrent;
using Moonshare.Server.WebSocketHandlers;
using System.Linq;

namespace Moonshare.Server.Managers
{
    public static class SessionManager
    {
        public static ConcurrentDictionary<string, AuthSession> Sessions = new();
        public static ConcurrentDictionary<string, PlayerBehavior> ConnectedPlayers = new();

        public static bool ValidateSession(string token, out AuthSession? session)
        {
            return Sessions.TryGetValue(token, out session!);
        }

        public static AuthSession[] GetOnlinePlayers()
        {
            return ConnectedPlayers.Values
                .Select(pb => Sessions.Values.FirstOrDefault(s => s.UserId == pb.UserId))
                .Where(s => s != null)
                .ToArray()!;
        }
    }
}
