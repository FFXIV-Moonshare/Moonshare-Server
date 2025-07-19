using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

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

    public static AuthSession[] GetAllSessions() => Sessions.Values.ToArray();

    public static bool ValidateSession(string token, out AuthSession? session)
        => Sessions.TryGetValue(token, out session!);

    public static string GetSessionsJson()
        => JsonSerializer.Serialize(Sessions.Values);

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
            Console.WriteLine("[AuthServer] Sessions sent via WebSocket");
        }
        else
        {
            Send("ERROR:UnknownCommand");
        }
    }
}

class Program
{
    static HttpListener? httpListener;

    static async Task Main()
    {
        var wssv = new WebSocketServer("ws://localhost:5004"); // WebSocket port 5004
        wssv.AddWebSocketService<AuthBehavior>("/auth");
        wssv.AddWebSocketService<SessionQueryBehavior>("/sessions");
        wssv.Start();
        Console.WriteLine("✅ WebSocket AuthServer läuft auf ws://localhost:5004/auth und /sessions");

        httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://localhost:5003/sessions/");
        httpListener.Start();
        Console.WriteLine("✅ HTTP AuthServer läuft auf http://localhost:5003/sessions/");

        var cleanupTimer = new System.Timers.Timer(60000);
        cleanupTimer.Elapsed += (_, _) => SessionManager.CleanupExpiredSessions(TimeSpan.FromMinutes(10));
        cleanupTimer.Start();

        var httpTask = Task.Run(async () =>
        {
            while (httpListener.IsListening)
            {
                try
                {
                    var ctx = await httpListener.GetContextAsync();
                    ProcessHttpRequest(ctx);
                }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HTTP] Fehler: {ex}");
                }
            }
        });

        Console.WriteLine("Drücke Enter zum Stoppen...");
        Console.ReadLine();

        httpListener.Stop();
        wssv.Stop();
    }

    private static void ProcessHttpRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        if (req.HttpMethod != "GET")
        {
            res.StatusCode = 405;
            res.Close();
            return;
        }

        var query = req.QueryString;
        var userId = query["userId"];

        if (string.IsNullOrWhiteSpace(userId))
        {
            res.StatusCode = 400;
            var errorBytes = Encoding.UTF8.GetBytes("{\"error\":\"Missing userId parameter\"}");
            res.OutputStream.Write(errorBytes, 0, errorBytes.Length);
            res.Close();
            return;
        }

        var token = SessionManager.GenerateSession(userId!);

        var json = JsonSerializer.Serialize(new { token });

        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json";
        res.ContentEncoding = Encoding.UTF8;
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.Close();

        Console.WriteLine($"[HTTP] Token für userId '{userId}' ausgegeben.");
    }
}
