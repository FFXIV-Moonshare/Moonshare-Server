using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Moonshare.Server.Models;
using Serilog;

namespace Moonshare.Server.Managers
{
    public interface IActiveSessionProvider
    {
        IEnumerable<string> GetActiveSessionTokens();
    }

    public static class SessionManager
    {
        public static bool SessionLoggingEnabled { get; set; } = true;

        public static ConcurrentDictionary<string, AuthSession> Sessions { get; } = new();
        private static ConcurrentDictionary<string, string> ClientAddressToToken { get; } = new();
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

            if (ClientAddressToToken.TryGetValue(clientAddress, out var oldToken) && oldToken != token)
            {
                if (Sessions.TryGetValue(oldToken, out var oldSession))
                {
                    oldSession.IsActive = false;
                    Sessions.TryRemove(oldToken, out _);
                    if (SessionLoggingEnabled)
                        Log.Information("[AuthServer] Removed old session {OldToken} for Client {ClientAddress}", oldToken, clientAddress);
                }
            }

            ClientAddressToToken[clientAddress] = token;

            if (SessionLoggingEnabled)
                Log.Information("[AuthServer] Session created for {UserId} with token {Token} from {ClientAddress}", userId, token, clientAddress);

            return token;
        }

        public static void MarkSessionInactive(string token)
        {
            if (Sessions.TryGetValue(token, out var session))
            {
                session.IsActive = false;
                ClientAddressToToken.TryRemove(session.ClientAddress, out _);
                if (SessionLoggingEnabled)
                    Log.Information("[AuthServer] Marked session {Token} inactive (UserId: {UserId})", token, session.UserId);
            }
        }

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
                    if (SessionLoggingEnabled)
                        Log.Information("[AuthServer] Removed inactive session {Token} (UserId: {UserId})", token, session.UserId);
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
                if (SessionLoggingEnabled)
                    Log.Debug("[AuthServer] Refreshed session {Token} (UserId: {UserId})", token, session.UserId);
                return true;
            }
            if (SessionLoggingEnabled)
                Log.Warning("[AuthServer] Attempted to refresh non-existent session {Token}", token);
            return false;
        }

        public static void RemoveSession(string token)
        {
            if (Sessions.TryRemove(token, out var session))
            {
                ClientAddressToToken.TryRemove(session.ClientAddress, out _);
                if (SessionLoggingEnabled)
                    Log.Information("[AuthServer] Session {Token} removed (UserId: {UserId})", token, session.UserId);
            }
        }

        public static string GetSessionsJson()
            => JsonSerializer.Serialize(Sessions.Values);
    }
}
