using System;
using WebSocketSharp;
using WebSocketSharp.Server;

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

        // Session erzeugen
        var token = SessionManager.GenerateSession(userId);

        // Antwort senden
        Send($"AUTH_SUCCESS:{token}");
        Console.WriteLine($"[AuthServer] Authenticated user '{userId}' with token '{token}'");
    }
}

class AuthServer
{
    static void Main()
    {
        var wssv = new WebSocketServer("ws://localhost:5001");
        wssv.AddWebSocketService<AuthBehavior>("/auth");
        wssv.Start();

        Console.WriteLine("✅ AuthServer läuft auf ws://localhost:5001/auth");

        // Optional: regelmäßige Cleanup-Logik starten
        var cleanupTimer = new System.Timers.Timer(60000); // alle 60 Sekunden
        cleanupTimer.Elapsed += (_, _) => SessionManager.CleanupExpiredSessions(TimeSpan.FromMinutes(10));
        cleanupTimer.Start();

        Console.ReadLine();
        wssv.Stop();
    }
}
