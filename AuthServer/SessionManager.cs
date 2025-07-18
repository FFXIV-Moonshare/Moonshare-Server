// Anpassung: AuthServer muss auch einen "/sessions" Endpoint bereitstellen
// Dieser sendet auf "GET_SESSIONS" alle aktiven Sessions als JSON

using System;
using System.Collections.Concurrent;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Text.Json;

public class AuthSession
{
    public string UserId { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public static class SessionManager
{
    public static ConcurrentDictionary<string, AuthSession> Sessions = new();

    public static string GenerateSession(string userId)
    {
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

    public static AuthSession[] GetAllSessions()
    {
        return Sessions.Values.ToArray();
    }


    public static bool ValidateSession(string token, out AuthSession? session)
    {
        return Sessions.TryGetValue(token, out session!);
    }

    public static string GetSessionsJson()
    {
        return JsonSerializer.Serialize(Sessions.Values);
    }

    public static void CleanupExpiredSessions(TimeSpan maxAge)
    {
        var expiredTokens = new System.Collections.Generic.List<string>();
        foreach (var kvp in Sessions)
        {
            if (DateTime.UtcNow - kvp.Value.CreatedAt > maxAge)
                expiredTokens.Add(kvp.Key);
        }
        foreach (var token in expiredTokens)
        {
            Sessions.TryRemove(token, out _);
            Console.WriteLine($"[AuthServer] Session {token} expired and removed.");
        }
    }
}

public class AuthBehavior : WebSocketBehavior
{
    protected override void OnMessage(MessageEventArgs e)
    {
        var userId = e.Data.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            Send("AUTH_FAIL:EmptyUserId");
            Context.WebSocket.Close();
            return;
        }
        var token = SessionManager.GenerateSession(userId);
        Send($"AUTH_SUCCESS:{token}");
        Console.WriteLine($"[AuthServer] Authenticated user '{userId}' with token '{token}'");
    }
}

public class SessionQueryBehavior : WebSocketBehavior
{
    protected override void OnMessage(MessageEventArgs e)
    {
        if (e.Data == "GET_SESSIONS")
        {
            var json = SessionManager.GetSessionsJson();
            Send(json);
            Console.WriteLine("[AuthServer] Sessions gesendet an PlayerServer");
        }
        else
        {
            Send("ERROR:UnknownCommand");
        }
    }
}

class AuthServer
{
    static void Main()
    {
        var wssv = new WebSocketServer("ws://localhost:5001");
        wssv.AddWebSocketService<AuthBehavior>("/auth");
        wssv.AddWebSocketService<SessionQueryBehavior>("/sessions");
        wssv.Start();

        Console.WriteLine("✅ AuthServer läuft auf ws://localhost:5001/auth und /sessions");

        var cleanupTimer = new System.Timers.Timer(60000);
        cleanupTimer.Elapsed += (_, _) => SessionManager.CleanupExpiredSessions(TimeSpan.FromMinutes(10));
        cleanupTimer.Start();

        Console.ReadLine();
        wssv.Stop();
    }
}
