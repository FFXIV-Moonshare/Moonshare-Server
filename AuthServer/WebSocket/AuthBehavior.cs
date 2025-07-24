using System;
using Serilog;
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
                Serilog.Log.Warning("[AuthBehavior] Connection rejected: missing userId from {ClientAddress}", _clientAddress);
                Context.WebSocket.Close(CloseStatusCode.PolicyViolation, "Missing userId");
                return;
            }

            _sessionToken = SessionManager.GenerateSession(userId, _clientAddress);

            Serilog.Log.Information("[AuthBehavior] New WebSocket connection from {ClientAddress} (UserId: {UserId}, Token: {SessionToken})",
                _clientAddress, userId, _sessionToken);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_sessionToken))
            {
                SessionManager.MarkSessionInactive(_sessionToken);

                Serilog.Log.Information("[AuthBehavior] WebSocket connection closed (Token: {SessionToken}, Reason: {Reason})",
                    _sessionToken, e.Reason);
            }
            else
            {
                Log.Debug("[AuthBehavior] Connection closed with no active session");
            }
        }
    }
}
