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

        public static List<IActiveSessionProvider> ActiveSessionProviders { get; } = new();

        public static string GenerateSession(string userId, string clientAddress)
        {
            string newToken = Guid.NewGuid().ToString("N");

            var newSession = new AuthSession
            {
                UserId = userId,
                SessionToken = newToken,
                CreatedAt = DateTime.UtcNow,
                ClientAddress = clientAddress,
                IsActive = true
            };

            Sessions[newToken] = newSession;

            if (SessionLoggingEnabled)
                Log.Information("[AuthServer] New session created for {UserId}: {Token} from {ClientAddress}", userId, newToken, clientAddress);

            return newToken;
        }

        public static void MarkSessionInactive(string token)
        {
            if (Sessions.TryGetValue(token, out var session))
            {
                session.IsActive = false;

                if (SessionLoggingEnabled)
                    Log.Information("[AuthServer] Marked session {Token} inactive (UserId: {UserId})", token, session.UserId);
            }
            else if (SessionLoggingEnabled)
            {
                Log.Warning("[AuthServer] Tried to mark non-existent session {Token} inactive", token);
            }
        }

        public static void CleanupInactiveSessions()
        {
            var activeTokens = new HashSet<string>(
                ActiveSessionProviders.SelectMany(p => p.GetActiveSessionTokens())
            );

            var tokensToRemove = Sessions
                .Where(kvp => !kvp.Value.IsActive && !activeTokens.Contains(kvp.Key))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var token in tokensToRemove)
            {
                if (Sessions.TryRemove(token, out var session))
                {
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
                if (SessionLoggingEnabled)
                    Log.Information("[AuthServer] Session {Token} removed (UserId: {UserId})", token, session.UserId);
            }
            else if (SessionLoggingEnabled)
            {
                Log.Warning("[AuthServer] Tried to remove non-existent session {Token}", token);
            }
        }

        public static string GetSessionsJson()
        {
            return JsonSerializer.Serialize(Sessions.Values);
        }

        public static bool SessionExists(string token)
        {
            return Sessions.ContainsKey(token);
        }

        public static AuthSession? GetSession(string token)
        {
            return Sessions.TryGetValue(token, out var session) ? session : null;
        }
    }
}
