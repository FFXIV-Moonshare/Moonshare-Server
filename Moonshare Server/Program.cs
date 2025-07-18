using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

public class PlayerBehavior : WebSocketBehavior
{
    private string _userId = string.Empty;

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
    static async Task Main()
    {
        // 1) Starte WebSocketServer für Clients
        var wssv = new WebSocketServer("ws://localhost:5002");
        wssv.AddWebSocketService<PlayerBehavior>("/player");
        wssv.Start();

        Console.WriteLine("🎮 PlayerServer läuft auf ws://localhost:5002/player");

        // 2) Verbinde dich als WebSocketSharp-Client zum AuthServer und hole Sessions
        await ConnectToAuthServerAndFetchSessions();

        Console.ReadLine();
        wssv.Stop();
    }

    static TaskCompletionSource<string>? tcs;

    static Task ConnectToAuthServerAndFetchSessions()
    {
        tcs = new TaskCompletionSource<string>();

        var ws = new WebSocket("ws://localhost:5001/sessions");

        ws.OnOpen += (sender, e) =>
        {
            Console.WriteLine("[PlayerServer] Verbunden mit AuthServer /sessions, sende GET_SESSIONS");
            ws.Send("GET_SESSIONS");
        };

        ws.OnMessage += (sender, e) =>
        {
            Console.WriteLine("[PlayerServer] Antwort vom AuthServer erhalten");
            tcs.TrySetResult(e.Data);
        };

        ws.OnError += (sender, e) =>
        {
            Console.WriteLine($"[PlayerServer] WebSocket Fehler: {e.Message}");
            tcs.TrySetException(new Exception(e.Message));
        };

        ws.Connect();

        return tcs.Task.ContinueWith(task =>
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                var json = task.Result;
                Console.WriteLine("[PlayerServer] Sessions vom AuthServer erhalten:");
                Console.WriteLine(json);

                try
                {
                    var sessions = JsonSerializer.Deserialize<AuthSession[]>(json);
                    if (sessions != null)
                    {
                        foreach (var s in sessions)
                        {
                            Console.WriteLine($"UserId: {s.UserId}, Token: {s.SessionToken}, CreatedAt: {s.CreatedAt}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PlayerServer] Fehler beim Parsen der Sessions: {ex}");
                }
            }
        });
    }
}


