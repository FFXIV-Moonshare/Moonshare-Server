using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Moonshare.Server.Models;

namespace Moonshare.Server.Managers
{
    public interface IActiveSessionProvider
    {
        IEnumerable<string> GetActiveSessionTokens();
    }

    public static class SessionManager
    {
        public static ConcurrentDictionary<string, AuthSession> Sessions = new();

       
        private static ConcurrentDictionary<string, string> ClientAddressToToken = new();
        // Key = ClientAddress, Value = SessionToken

       
        public static List<IActiveSessionProvider> ActiveSessionProviders { get; } = new();

        public static string GenerateSession(string userId, string clientAddress)
        {
            var existing = Sessions.Values.FirstOrDefault(s => s.UserId == userId);
            if (existing != null)
                return existing.SessionToken;

            string token = Guid.NewGuid().ToString("N");

            var newSession = new AuthSession
            {
                UserId = userId,
                SessionToken = token,
                CreatedAt = DateTime.UtcNow,
                ClientAddress = clientAddress,
                IsActive = true
            };

            Sessions[token] = newSession;

            // Doppel-Session Check:
            if (ClientAddressToToken.TryGetValue(clientAddress, out var oldToken))
            {
                if (oldToken != token)
                {
                    // Alte Session als inaktiv markieren und entfernen
                    if (Sessions.TryGetValue(oldToken, out var oldSession))
                    {
                        oldSession.IsActive = false;
                        Sessions.TryRemove(oldToken, out _);
                        Console.WriteLine($"[AuthServer] Removed old session {oldToken} for Client {clientAddress}");
                    }
                }
            }

            ClientAddressToToken[clientAddress] = token;

            Console.WriteLine($"[AuthServer] Session created for {userId} with token {token} from {clientAddress}");
            return token;
        }

        public static void MarkSessionInactive(string token)
        {
            if (Sessions.TryGetValue(token, out var session))
            {
                session.IsActive = false;
                ClientAddressToToken.TryRemove(session.ClientAddress, out _);
                Console.WriteLine($"[AuthServer] Marked session {token} inactive (UserId: {session.UserId})");
            }
        }

        /// <summary>
        /// Entfernt Sessions, die nicht aktiv sind UND nicht in PlayerServer/anderen Session-Providern mehr vorhanden sind
        /// </summary>
        public static void CleanupInactiveSessions()
        {
            var activeTokensFromProviders = new HashSet<string>(
                ActiveSessionProviders.SelectMany(p => p.GetActiveSessionTokens())
            );

            var tokensToRemove = Sessions
                .Where(kvp => !kvp.Value.IsActive && !activeTokensFromProviders.Contains(kvp.Key))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var token in tokensToRemove)
            {
                if (Sessions.TryRemove(token, out var session))
                {
                    ClientAddressToToken.TryRemove(session.ClientAddress, out _);
                    Console.WriteLine($"[AuthServer] Removed inactive session {token} (UserId: {session.UserId})");
                }
            }
        }

        public static bool RefreshSession(string token)
        {
            if (Sessions.TryGetValue(token, out var session))
            {
                session.CreatedAt = DateTime.UtcNow;
                session.IsActive = true;
                CleanupInactiveSessions();
                return true;
            }
            return false;
        }

        public static void RemoveSession(string token)
        {
            if (Sessions.TryRemove(token, out var session))
            {
                ClientAddressToToken.TryRemove(session.ClientAddress, out _);
                Console.WriteLine($"[AuthServer] Session {token} removed (UserId: {session.UserId})");
            }
        }

        public static string GetSessionsJson()
            => JsonSerializer.Serialize(Sessions.Values);
    }
}
