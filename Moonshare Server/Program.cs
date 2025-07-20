using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
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

    public static bool ValidateSession(string token, out AuthSession? session)
    {
        return Sessions.TryGetValue(token, out session!);
    }
}

public class PlayerBehavior : WebSocketBehavior
{
    private string _userId = string.Empty;

    protected override void OnMessage(MessageEventArgs e)
    {
        var message = e.Data;

        if (message.StartsWith("SESSION:"))
        {
            var token = message.Substring("SESSION:".Length);

            if (SessionManager.ValidateSession(token, out var session))
            {
                _userId = session.UserId;
                Send($"SESSION_OK:{_userId}");
                EventLogManager.LogInfo($"{_userId} connected with valid session");
            }
            else
            {
                Send("SESSION_INVALID");
                EventLogManager.LogError("Invalid session token received. Connection closed.");
                Context.WebSocket.Close();
            }
        }
        else
        {
            EventLogManager.LogInfo($"{_userId}: {message}");
            Send($"ECHO ({_userId}): {message}");
        }
    }

    protected override void OnOpen()
    {
        EventLogManager.LogInfo($"New WebSocket connection from {Context.UserEndPoint}");
    }

    protected override void OnClose(CloseEventArgs e)
    {
        EventLogManager.LogInfo($"Connection closed for user {_userId}");
    }
}

public static class PlayerServer
{
    private static WebSocket? authServerSocket;
    private static Timer? sessionUpdateTimer;

    public static Task StartAsync()
    {
        var wssv = new WebSocketServer("ws://62.68.75.23:5002");
        wssv.AddWebSocketService<PlayerBehavior>("/player");
        wssv.Start();

        EventLogManager.LogInfo("🎮 PlayerServer running on ws://62.68.75.23:5002/player");

        ConnectAndStartSessionUpdates();

       
        EventLogManager.LogInfo("API läuft unter http://62.68.75.23:8080/");
        EventLogManager.LogInfo("Press Enter to stop PlayerServer...");
        Console.ReadLine();

        sessionUpdateTimer?.Dispose();
        authServerSocket?.Close();
        wssv.Stop();

        return Task.CompletedTask;
    }

    private static void ConnectAndStartSessionUpdates()
    {
        authServerSocket = new WebSocket("ws://62.68.75.23:5004/sessions");

        authServerSocket.OnOpen += (sender, e) =>
        {
            EventLogManager.LogInfo("Connected to AuthServer /sessions");
            RequestSessions();
            StartSessionUpdateTimer();
        };

        authServerSocket.OnMessage += (sender, e) =>
        {
            EventLogManager.LogInfo("Sessions received from AuthServer");
            UpdateSessions(e.Data);
        };

        authServerSocket.OnError += (sender, e) =>
        {
            EventLogManager.LogError($"WebSocket error: {e.Message}");
        };

        authServerSocket.OnClose += (sender, e) =>
        {
            EventLogManager.LogError("AuthServer connection closed. Reconnecting in 5s...");
            sessionUpdateTimer?.Dispose();
            Task.Delay(5000).ContinueWith(_ => ConnectAndStartSessionUpdates());
        };

        authServerSocket.Connect();
    }

    private static void RequestSessions()
    {
        if (authServerSocket?.ReadyState == WebSocketState.Open)
            authServerSocket.Send("GET_SESSIONS");
    }

    private static void StartSessionUpdateTimer()
    {
        sessionUpdateTimer = new Timer(_ =>
        {
            RequestSessions();
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private static void UpdateSessions(string json)
    {
        try
        {
            var sessions = JsonSerializer.Deserialize<AuthSession[]>(json);
            if (sessions != null)
            {
                SessionManager.Sessions.Clear();
                foreach (var s in sessions)
                    SessionManager.Sessions[s.SessionToken] = s;

                EventLogManager.LogInfo($"{sessions.Length} sessions updated from AuthServer.");
            }
        }
        catch (Exception ex)
        {
            EventLogManager.LogError($"Error updating sessions: {ex}");
        }
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            EventLogManager.LogInfo("No argument specified, starting PlayerServer by default.");
            await PlayerServer.StartAsync();
            return;
        }

        if (args[0].Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            await PlayerServer.StartAsync();
        }
        else
        {
            EventLogManager.LogError("Unknown argument. Use 'player'");
        }
    }
}
