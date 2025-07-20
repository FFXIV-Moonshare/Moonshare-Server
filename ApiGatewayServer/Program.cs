using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;

public static class ApiGatewayServer
{
    private static HttpListener? _listener;

    // Aktuelle Sessions vom AuthServer
    private static ConcurrentDictionary<string, AuthSession> _authSessions = new();

    // Spieleranzahl vom PlayerServer (kann angepasst werden)
    private static int _playerCount = 0;

    // WebSocket-Verbindungen zu Auth- und Player-Server
    private static WebSocket? _authServerSocket;
    private static WebSocket? _playerServerSocket;

    public static async Task StartAsync()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://62.68.75.23:8090/"); // API auf Port 8090
        _listener.Start();

        Console.WriteLine("[ApiGatewayServer] HTTP API läuft auf http://62.68.75.23:8090/");

        ConnectToAuthServer();
        ConnectToPlayerServer();

        while (_listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleHttpRequest(context));
            }
            catch (HttpListenerException)
            {
                break; // Listener gestoppt
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ApiGatewayServer] HTTP Fehler: " + ex);
            }
        }
    }

    private static void ConnectToAuthServer()
    {
        _authServerSocket = new WebSocket("ws://62.68.75.23:5004/sessions");

        _authServerSocket.OnOpen += (sender, e) =>
        {
            Console.WriteLine("[ApiGatewayServer] Verbunden mit AuthServer");
            _authServerSocket.Send("GET_SESSIONS");
        };

        _authServerSocket.OnMessage += (sender, e) =>
        {
            try
            {
                var sessions = JsonSerializer.Deserialize<AuthSession[]>(e.Data);
                if (sessions != null)
                {
                    _authSessions.Clear();
                    foreach (var s in sessions)
                        _authSessions[s.SessionToken] = s;

                    Console.WriteLine($"[ApiGatewayServer] {_authSessions.Count} Sessions vom AuthServer empfangen");
                    EventLogManager.LogInfo($"Empfangen {_authSessions.Count} Sessions vom AuthServer");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ApiGatewayServer] Fehler bei Sessions vom AuthServer: " + ex);
                EventLogManager.LogError("Fehler bei Sessions vom AuthServer: " + ex.Message);
            }
        };

        _authServerSocket.OnClose += (sender, e) =>
        {
            Console.WriteLine("[ApiGatewayServer] Verbindung zu AuthServer geschlossen, versuche neu in 5s...");
            EventLogManager.LogInfo("Verbindung zu AuthServer geschlossen, erneuter Verbindungsversuch in 5s...");
            Task.Delay(5000).ContinueWith(_ => ConnectToAuthServer());
        };

        _authServerSocket.OnError += (sender, e) =>
        {
            Console.WriteLine("[ApiGatewayServer] WebSocket Fehler AuthServer: " + e.Message);
            EventLogManager.LogError("WebSocket Fehler AuthServer: " + e.Message);
        };

        _authServerSocket.Connect();
    }

    private static void ConnectToPlayerServer()
    {
        _playerServerSocket = new WebSocket("ws://62.68.75.23:5002/player");

        _playerServerSocket.OnOpen += (sender, e) =>
        {
            Console.WriteLine("[ApiGatewayServer] Verbunden mit PlayerServer");
            _playerServerSocket.Send("PING");
        };

        _playerServerSocket.OnMessage += (sender, e) =>
        {
            Console.WriteLine("[ApiGatewayServer] Nachricht vom PlayerServer: " + e.Data);
            EventLogManager.LogInfo("Nachricht vom PlayerServer: " + e.Data);

            if (e.Data.StartsWith("PLAYER_COUNT:"))
            {
                var countStr = e.Data.Substring("PLAYER_COUNT:".Length);
                if (int.TryParse(countStr, out int count))
                    _playerCount = count;
            }
        };

        _playerServerSocket.OnClose += (sender, e) =>
        {
            Console.WriteLine("[ApiGatewayServer] Verbindung zu PlayerServer geschlossen, versuche neu in 5s...");
            EventLogManager.LogInfo("Verbindung zu PlayerServer geschlossen, erneuter Verbindungsversuch in 5s...");
            Task.Delay(5000).ContinueWith(_ => ConnectToPlayerServer());
        };

        _playerServerSocket.OnError += (sender, e) =>
        {
            Console.WriteLine("[ApiGatewayServer] WebSocket Fehler PlayerServer: " + e.Message);
            EventLogManager.LogError("WebSocket Fehler PlayerServer: " + e.Message);
        };

        _playerServerSocket.Connect();
    }

    private static async Task HandleHttpRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        try
        {
            string json = "";
            int status = 200;

            switch (req.Url?.AbsolutePath)
            {
                case "/sessions":
                    json = JsonSerializer.Serialize(_authSessions.Values);
                    break;

                case "/players/count":
                    json = JsonSerializer.Serialize(new { count = _playerCount });
                    break;

                case "/events/recent":
                case "/logs":
                    var events = EventLogManager.GetRecentEvents();
                    json = JsonSerializer.Serialize(events);
                    break;

                default:
                    status = 404;
                    json = JsonSerializer.Serialize(new { error = "Not Found" });
                    break;
            }

            var buffer = Encoding.UTF8.GetBytes(json);
            res.StatusCode = status;
            res.ContentType = "application/json";
            res.ContentLength64 = buffer.Length;
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ApiGatewayServer] Fehler in HandleHttpRequest: " + ex);
            res.StatusCode = 500;
        }
        finally
        {
            res.Close();
        }
    }
}

public class AuthSession
{
    public string UserId { get; set; } = "";
    public string SessionToken { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public static class EventLogManager
{
    private static readonly ConcurrentQueue<string> _events = new();
    private const int MaxEvents = 200;

    public static void LogInfo(string message)
        => Log($"[INFO] {message}");

    public static void LogError(string message)
        => Log($"[ERROR] {message}");

    public static void LogDebug(string message)
        => Log($"[DEBUG] {message}");

    private static void Log(string message)
    {
        var formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.WriteLine(formatted);

        if (_events.Count >= MaxEvents)
            _events.TryDequeue(out _);

        _events.Enqueue(formatted);
    }

    public static List<string> GetRecentEvents()
        => _events.ToList();
}

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starte ApiGatewayServer...");
        await ApiGatewayServer.StartAsync();
    }
}
