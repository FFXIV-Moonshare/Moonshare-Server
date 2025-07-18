using System;
using System.Collections.Concurrent;

public class AuthSession
{
    public string UserId { get; set; }
    public string SessionToken { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class SessionManager
{
    private static readonly ConcurrentDictionary<string, AuthSession> Sessions = new();

    public static string GenerateSession(string userId)
    {
        string token = Guid.NewGuid().ToString("N");
        Sessions[token] = new AuthSession
        {
            UserId = userId,
            SessionToken = token,
            CreatedAt = DateTime.UtcNow
        };

        Console.WriteLine($"[SessionManager] Session created for {userId} with token {token}");
        return token;
    }

    public static bool ValidateSession(string token, out AuthSession session)
    {
        return Sessions.TryGetValue(token, out session);
    }

    public static void RevokeSession(string token)
    {
        Sessions.TryRemove(token, out _);
        Console.WriteLine($"[SessionManager] Session revoked: {token}");
    }

    public static void CleanupExpiredSessions(TimeSpan maxAge)
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in Sessions)
        {
            if (now - kvp.Value.CreatedAt > maxAge)
            {
                Sessions.TryRemove(kvp.Key, out _);
                Console.WriteLine($"[SessionManager] Session expired: {kvp.Key}");
            }
        }
    }
}
