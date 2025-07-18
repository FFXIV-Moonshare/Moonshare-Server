using System;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Net.Http;
using System.Text.Json;

public class PlayerBehavior : WebSocketBehavior
{
    private string _userId;

    protected override void OnMessage(MessageEventArgs e)
    {
        var message = e.Data;

        if (message.StartsWith("SESSION:"))
        {
            var token = message.Substring(8);

            if (SessionManager.ValidateSession(token, out var session))
            {
                _userId = session.UserId;
                Send($"SESSION_OK:{_userId}");
                Console.WriteLine($"[PlayerServer] {_userId} verbunden mit gültiger Session");
            }
            else
            {
                Send("SESSION_INVALID");
                Context.WebSocket.Close();
            }
        }
        else
        {
            Console.WriteLine($"[PlayerServer] {_userId}: {message}");
            Send($"ECHO ({_userId}): {message}");
        }
    }
}

class PlayerServer
{
    static void Main()
    {
        var wssv = new WebSocketServer("ws://localhost:5002");
        wssv.AddWebSocketService<PlayerBehavior>("/player");
        wssv.Start();

        Console.WriteLine("🎮 PlayerServer läuft auf ws://localhost:5002/player");
        Console.ReadLine();
        wssv.Stop();
    }
}
