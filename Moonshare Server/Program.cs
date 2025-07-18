using System;
using WebSocketSharp;
using WebSocketSharp.Server;

public class MoonshareBehavior : WebSocketBehavior
{
    protected override void OnOpen()
    {
        try
        {
            Console.WriteLine($"Client connected: {ID}");
            var registerMsg = $"{{\"type\":\"register\", \"userId\":\"{ID}\"}}";
            Send(registerMsg);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OnOpen] Fehler: {ex}");
        }
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        try
        {
            Console.WriteLine($"Received from {ID}: {e.Data}");

            // Hier kannst du Message-Parsing und Weiterleitung einbauen
            // Beispiel: Echo zurück zum Client
            var response = $"{{\"type\":\"message\", \"fromUserId\":\"{ID}\", \"payload\":\"{EscapeJson(e.Data)}\"}}";
            Send(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OnMessage] Fehler bei Nachricht von {ID}: {ex}");
        }
    }

    protected override void OnClose(CloseEventArgs e)
    {
        try
        {
            Console.WriteLine($"Client {ID} disconnected: {e.Reason} (Code: {e.Code})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OnClose] Fehler: {ex}");
        }
    }

    protected override void OnError(WebSocketSharp.ErrorEventArgs e)
    {
        Console.WriteLine($"[OnError] Fehler bei Client {ID}: {e.Message}");
    }

    // Hilfsmethode um JSON-Inhalt sicher zu escapen
    private static string EscapeJson(string str)
    {
        return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}

class Program
{
    static void Main()
    {
        var wssv = new WebSocketServer("ws://localhost:5000");

        wssv.AddWebSocketService<MoonshareBehavior>("/ws");

        try
        {
            wssv.Start();
            Console.WriteLine("Moonshare WebSocket Server läuft auf ws://localhost:5000/ws");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Fehler beim Starten: {ex}");
        }

        // Hauptloop, damit Server nicht sofort schließt
        Console.WriteLine("Drücke ENTER zum Beenden...");
        while (true)
        {
            var input = Console.ReadLine();
            if (input != null && input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;
        }

        try
        {
            wssv.Stop();
            Console.WriteLine("Server gestoppt.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Fehler beim Stoppen: {ex}");
        }
    }
}
