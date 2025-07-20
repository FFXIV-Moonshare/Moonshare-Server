using Moonshare.Server.Managers;
using Moonshare.Server.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Collections.Generic;

namespace Moonshare.Server.WebSocketHandlers
{
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
                        case "session_auth": HandleSessionAuth(root); break;
                        case "list_online": SendOnlineList(); break;
                        case "file_send_begin": HandleFileSendBegin(root); break;
                        case "file_send_complete": HandleFileSendComplete(root); break;
                        default: EventLogManager.LogInfo($"Unhandled message type: {type}"); break;
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

            Send("file_receive_ready");
            EventLogManager.LogInfo($"Started receiving file '{receivingFileName}' ({receivingFileSize} bytes) from user {UserId}");
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

            var target = SessionManager.ConnectedPlayers.Values.FirstOrDefault(p => p.UserId == receivingTargetUser);
            if (target != null)
            {
                var headerObj = new { type = "file_from", fromUserId = UserId, fileName = receivingFileName, fileSize = receivingFileSize };
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
}
