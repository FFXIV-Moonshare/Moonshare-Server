using System;
using WebSocketSharp;
using WebSocketSharp.Server;
using Moonshare.Server.Managers;

namespace Moonshare.Server.WebSocket
{
    public class AuthBehavior : WebSocketBehavior
    {
        private string? _sessionToken;
        private string? _clientAddress;

        protected override void OnOpen()
        {
            _clientAddress = Context.UserEndPoint?.Address.ToString() ?? "unknown";

            
            var userId = Context.QueryString["userId"];
            if (string.IsNullOrWhiteSpace(userId))
            {
                Console.WriteLine("[AuthBehavior] Connection rejected: missing userId");
                Context.WebSocket.Close(CloseStatusCode.PolicyViolation, "Missing userId");
                return;
            }

            _sessionToken = SessionManager.GenerateSession(userId, _clientAddress);
            Console.WriteLine($"[AuthBehavior] New connection from {_clientAddress} with token {_sessionToken}");
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if (_sessionToken != null)
            {
                SessionManager.MarkSessionInactive(_sessionToken);
                Console.WriteLine($"[AuthBehavior] Connection closed for session {_sessionToken}");
            }
        }
    }
}