using Moonshare.Server.Managers;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Moonshare.Server.WebSocketHandlers
{
    public class PlayerBehavior : WebSocketBehavior
    {
        public string UserId { get; private set; } = string.Empty;
        public string ID => IDInternal ?? ""; // ConnectionId
        private string? IDInternal;

        private static readonly string ReceivedFilesFolder = Path.Combine(AppContext.BaseDirectory, "ReceivedFiles");

        private MemoryStream? fileBuffer;
        private string? receivingFileName;
        private string? receivingTargetUser;
        private long receivingFileSize;
        private long totalReceivedBytes;

        private readonly PlayerServerInstance _serverInstance;
        private readonly int _shardId;
        private bool _isConnected = false;

        public PlayerBehavior(PlayerServerInstance instance, int shardId)
        {
            _serverInstance = instance;
            _shardId = shardId;
        }

        protected override void OnOpen()
        {
            IDInternal = ID; // WebSocket-Verbindungs-ID setzen
            Serilog.Log.Information("New WebSocket connection from {EndPoint} on shard {ShardId}", Context.UserEndPoint, _shardId);
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
                    Log.Error("Received binary data but no file transfer active.");
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
                        case "session_auth": HandleSessionAuth(root); break;
                        case "list_online": SendOnlineList(); break;
                        case "file_send_begin": HandleFileSendBegin(root); break;
                        case "file_send_complete": HandleFileSendComplete(root); break;
                        default:
                            Serilog.Log.Information("Unhandled message type: {Type}", type);
                            break;
                    }
                }
                catch (JsonException)
                {
                    Log.Error("Failed to parse JSON message.");
                }
            }
            else
            {
                Serilog.Log.Information("Received non-JSON message: {Message}", message);
            }
        }

        private void HandleSessionAuth(JsonElement root)
        {
            if (!root.TryGetProperty("token", out var tokenElem))
            {
                Send(JsonSerializer.Serialize(new { type = "session_auth_result", success = false, message = "Missing token" }));
                Context.WebSocket.Close();
                return;
            }

            var token = tokenElem.GetString() ?? "";

            if (_serverInstance.SessionExists(token) && SessionManager.ValidateSession(token, out var session))
            {
                UserId = session.UserId;

                // Spieler hinzufügen über SessionManager (inkl. Warteschlange etc)
                bool accepted = SessionManager.AddConnectedPlayer(_shardId, ID, this);
                if (!accepted)
                {
                    Send(JsonSerializer.Serialize(new { type = "session_auth_result", success = false, message = "Server full, please try later" }));
                    Context.WebSocket.Close(CloseStatusCode.PolicyViolation, "Server full, please try later.");
                    Serilog.Log.Information("Rejected {UserId} due to max player limit on shard {ShardId}", UserId, _shardId);
                    return;
                }

                _isConnected = true;

                Send(JsonSerializer.Serialize(new { type = "session_auth_result", success = true, message = "Authentication successful", userId = UserId }));

                Serilog.Log.Information("{UserId} connected with valid session on shard {ShardId}", UserId, _shardId);
                _serverInstance.SendInstanceInfoToClient(Context.WebSocket);
            }
            else
            {
                Send(JsonSerializer.Serialize(new { type = "session_auth_result", success = false, message = "Invalid session token" }));
                Serilog.Log.Warning("Invalid session token received, closing connection.");
                Context.WebSocket.Close();
            }
        }

        private void SendOnlineList()
        {
            var online = SessionManager.GetOnlinePlayers(_shardId);
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

            Send("file_receive_ready");
            Serilog.Log.Information("Started receiving file '{FileName}' ({FileSize} bytes) from user {UserId} on shard {ShardId}", receivingFileName, receivingFileSize, UserId, _shardId);
        }

        private string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace("/", "_").Replace("\\", "_");
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
                Serilog.Log.Information("File size mismatch: expected {Expected} bytes, received {Received} bytes", receivingFileSize, totalReceivedBytes);
                fileBuffer.Dispose();
                ClearFileTransferState();
                return;
            }

            string saveFolder = Path.Combine(ReceivedFilesFolder, Guid.NewGuid().ToString());
            Directory.CreateDirectory(saveFolder);
            string path = Path.Combine(saveFolder, receivingFileName);
            File.WriteAllBytes(path, fileBuffer.ToArray());

            Serilog.Log.Information("File saved: {Path}", path);

            // Zielnutzer im selben Shard aus SessionManager.ActivePlayers ermitteln
            if (SessionManager.ActivePlayers.TryGetValue(_shardId, out var connected))
            {
                var target = connected.Values.FirstOrDefault(p => p.UserId == receivingTargetUser && p != this);
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
                    Serilog.Log.Information("File forwarded to {TargetUser} on shard {ShardId}", receivingTargetUser, _shardId);
                }
                else
                {
                    Send("FILE_SENT_SERVER_ONLY");
                    Serilog.Log.Information("Target user {TargetUser} not connected on shard {ShardId}, file saved on server only", receivingTargetUser, _shardId);
                }
            }
            else
            {
                Send("FILE_SENT_SERVER_ONLY");
                Serilog.Log.Information("No connected players found on shard {ShardId}", _shardId);
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
            if (_isConnected)
            {
                SessionManager.RemoveConnectedPlayer(_shardId, ID);
                _serverInstance.RemovePlayer(ID, UserId);
            }
            Serilog.Log.Information("Connection closed for user {UserId} on shard {ShardId}", UserId, _shardId);
        }
    }
}
