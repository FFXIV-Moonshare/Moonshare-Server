using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

#region Session Models

public class AuthSession
{
    public string UserId { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

#endregion

#region Session Manager

public static class SessionManager
{
    public static ConcurrentDictionary<string, AuthSession> Sessions = new();
    public static ConcurrentDictionary<string, PlayerBehavior> ConnectedPlayers = new();

    public static bool ValidateSession(string token, out AuthSession? session)
    {
        return Sessions.TryGetValue(token, out session!);
    }

    public static AuthSession[] GetOnlinePlayers()
    {
        return ConnectedPlayers.Values
            .Select(pb => Sessions.Values.FirstOrDefault(s => s.UserId == pb.UserId))
            .Where(s => s != null)
            .ToArray()!;
    }
}

#endregion

#region Player Behavior

public class PlayerBehavior : WebSocketBehavior
{
    public string UserId { get; private set; } = string.Empty;
    private static readonly string ReceivedFilesFolder = Path.Combine(AppContext.BaseDirectory, "ReceivedFiles");

    private MemoryStream? fileBuffer;
    private string? receivingFileName;
    private string? receivingTargetUser;
    private long receivingFileSize;
    private long totalReceivedBytes;


    protected override void OnOpen()
    {
        EventLogManager.LogInfo("New WebSocket connection from " + Context.UserEndPoint);

        if (!Directory.Exists(ReceivedFilesFolder))
            Directory.CreateDirectory(ReceivedFilesFolder);
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        if (e.IsBinary)
        {
            if (fileBuffer != null)
            {
                fileBuffer.Write(e.RawData);
                totalReceivedBytes += e.RawData.Length;
            }
            else
            {
                EventLogManager.LogError("Received binary data but no file transfer active.");
            }
            return;
        }

        var message = e.Data;
        if (string.IsNullOrEmpty(message)) return;

        var trimmed = message.TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeElem)) return;

                var type = typeElem.GetString();

                switch (type)
                {
                    case "session_auth":
                        HandleSessionAuth(root);
                        break;

                    case "list_online":
                        SendOnlineList();
                        break;

                    case "file_send_begin":
                        HandleFileSendBegin(root);
                        break;

                    case "file_send_complete":
                        HandleFileSendComplete(root);
                        break;

                    default:
                        EventLogManager.LogInfo($"Unhandled message type: {type}");
                        break;
                }
            }
            catch (JsonException)
            {
                EventLogManager.LogError("Failed to parse JSON message.");
            }
        }
        else
        {
            EventLogManager.LogInfo($"Received non-JSON message: {message}");
        }
    }

    private void HandleSessionAuth(JsonElement root)
    {
        if (!root.TryGetProperty("token", out var tokenElem))
        {
            Send("SESSION_INVALID");
            Context.WebSocket.Close();
            return;
        }

        var token = tokenElem.GetString() ?? "";

        if (SessionManager.ValidateSession(token, out var session))
        {
            UserId = session.UserId;
            SessionManager.ConnectedPlayers[ID] = this;
            Send($"SESSION_OK:{UserId}");
            EventLogManager.LogInfo($"{UserId} connected with valid session");
        }
        else
        {
            Send("SESSION_INVALID");
            Context.WebSocket.Close();
        }
    }

    private void SendOnlineList()
    {
        var online = SessionManager.GetOnlinePlayers();
        var json = JsonSerializer.Serialize(online);
        Send("ONLINE:" + json);
    }

    private void HandleFileSendBegin(JsonElement root)
    {
        if (!root.TryGetProperty("targetUserId", out var targetElem) ||
            !root.TryGetProperty("fileName", out var fileNameElem) ||
            !root.TryGetProperty("fileSize", out var fileSizeElem))
        {
            Send("FILE_FAILED: Missing file_send_begin parameters");
            return;
        }

        receivingTargetUser = targetElem.GetString();
        receivingFileName = SanitizeFileName(fileNameElem.GetString() ?? "unnamed");
        receivingFileSize = fileSizeElem.GetInt64();
        totalReceivedBytes = 0;
        fileBuffer = new MemoryStream();

        // ACK an Client - genau so wie Client es erwartet ("file_receive_ready" klein)
        Send("file_receive_ready");

        EventLogManager.LogInfo($"Started receiving file '{receivingFileName}' ({receivingFileSize} bytes) from user {UserId}");
    }

    private string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        // Pfad-Traversal verhindern
        name = name.Replace("/", "_").Replace("\\", "_");

        return name;
    }



    private void HandleFileSendComplete(JsonElement root)
    {
        if (fileBuffer == null || receivingFileName == null || receivingTargetUser == null)
        {
            Send("FILE_FAILED: Missing state for completion");
            return;
        }

        if (totalReceivedBytes != receivingFileSize)
        {
            Send("FILE_FAILED: Size mismatch");
            EventLogManager.LogError($"File size mismatch: expected {receivingFileSize}, received {totalReceivedBytes}");
            fileBuffer.Dispose();
            ClearFileTransferState();
            return;
        }

        string saveFolder = Path.Combine(ReceivedFilesFolder, Guid.NewGuid().ToString());
        Directory.CreateDirectory(saveFolder);
        string path = Path.Combine(saveFolder, receivingFileName);
        File.WriteAllBytes(path, fileBuffer.ToArray());
        EventLogManager.LogInfo($"File saved: {path}");

        // Datei an Zieluser weiterleiten, wenn verbunden
        var target = SessionManager.ConnectedPlayers.Values.FirstOrDefault(p => p.UserId == receivingTargetUser);
        if (target != null)
        {
            var headerObj = new
            {
                type = "file_from",
                fromUserId = UserId,
                fileName = receivingFileName,
                fileSize = receivingFileSize
            };
            var headerJson = JsonSerializer.Serialize(headerObj);

            target.Send(headerJson);
            target.Send(fileBuffer.ToArray());
            Send("FILE_SENT");
            EventLogManager.LogInfo($"File forwarded to {receivingTargetUser}");
        }
        else
        {
            Send("FILE_SENT_SERVER_ONLY");
            EventLogManager.LogInfo($"Target user {receivingTargetUser} not connected, file saved on server only");
        }

        fileBuffer.Dispose();
        ClearFileTransferState();
    }


    private void ClearFileTransferState()
    {
        fileBuffer = null;
        receivingFileName = null;
        receivingTargetUser = null;
        receivingFileSize = 0;
        totalReceivedBytes = 0;
    }

    protected override void OnClose(CloseEventArgs e)
    {
        SessionManager.ConnectedPlayers.TryRemove(ID, out _);
        EventLogManager.LogInfo("Connection closed for user " + UserId);
    }


}
#endregion

#region Player Server

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

        Console.ReadLine();

        sessionUpdateTimer?.Dispose();
        authServerSocket?.Close();
        wssv.Stop();

        return Task.CompletedTask;
    }

    private static void ConnectAndStartSessionUpdates()
    {
        authServerSocket = new WebSocket("ws://62.68.75.23:5004/sessions");

        authServerSocket.OnOpen += (s, e) =>
        {
            EventLogManager.LogInfo("Connected to AuthServer /sessions");
            RequestSessions();
            StartSessionUpdateTimer();
        };

        authServerSocket.OnMessage += (s, e) =>
        {
            UpdateSessions(e.Data);
        };

        authServerSocket.OnError += (s, e) =>
        {
            EventLogManager.LogError("WebSocket error: " + e.Message);
        };

        authServerSocket.OnClose += (s, e) =>
        {
            EventLogManager.LogError("AuthServer disconnected. Reconnecting in 5s...");
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

                EventLogManager.LogInfo(sessions.Length + " sessions updated from AuthServer.");
            }
        }
        catch (Exception ex)
        {
            EventLogManager.LogError("Error updating sessions: " + ex);
        }
    }
}

#endregion

#region Program Entry

class Program
{
    static async Task Main(string[] args)
    {
        await PlayerServer.StartAsync();
    }
}

#endregion
