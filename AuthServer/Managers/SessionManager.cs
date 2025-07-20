using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using Moonshare.Server.Models;

namespace Moonshare.Server.Managers
{
    public static class SessionManager
    {
        public static ConcurrentDictionary<string, AuthSession> Sessions = new();

        public static string GenerateSession(string userId)
        {
            var existing = Sessions.Values.FirstOrDefault(s => s.UserId == userId);
            if (existing != null)
                return existing.SessionToken;

            string token = Guid.NewGuid().ToString("N");
            Sessions[token] = new AuthSession
            {
                UserId = userId,
                SessionToken = token,
                CreatedAt = DateTime.UtcNow
            };
            Console.WriteLine($"[AuthServer] Session created for {userId} with token {token}");
            return token;
        }

        public static bool RefreshSession(string token)
        {
            if (Sessions.TryGetValue(token, out var session))
            {
                session.CreatedAt = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        public static void RemoveSession(string token)
        {
            if (Sessions.TryRemove(token, out var session))
            {
                Console.WriteLine($"[AuthServer] Session {token} removed (UserId: {session.UserId})");
            }
        }

        public static void CleanupExpiredSessions(TimeSpan maxAge)
        {
            var expiredTokens = Sessions
                .Where(kvp => DateTime.UtcNow - kvp.Value.CreatedAt > maxAge)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var token in expiredTokens)
            {
                Sessions.TryRemove(token, out _);
                Console.WriteLine($"[AuthServer] Session {token} expired and removed.");
            }
        }

        public static string GetSessionsJson()
            => JsonSerializer.Serialize(Sessions.Values);
    }
}
    