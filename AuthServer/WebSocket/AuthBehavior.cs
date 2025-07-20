using System;
using WebSocketSharp;
using WebSocketSharp.Server;
using Moonshare.Server.Managers;

namespace Moonshare.Server.WebSocket
{
    public class AuthBehavior : WebSocketBehavior
    {
        private string? SessionToken;

        protected override void OnMessage(MessageEventArgs e)
        {
            var message = e.Data.Trim();

            if (message.StartsWith("SESSION:"))
            {
                var token = message.Substring("SESSION:".Length);
                if (SessionManager.RefreshSession(token))
                {
                    SessionToken = token;
                    Send("HEARTBEAT_OK");
                    Console.WriteLine($"[AuthServer] Session {token} refreshed.");
                }
                else
                {
                    Send("SESSION_INVALID");
                    Console.WriteLine($"[AuthServer] Invalid session token received: {token}");
                    Context.WebSocket.Close();
                }
                return;
            }

            var userId = message;
            if (string.IsNullOrWhiteSpace(userId))
            {
                Send("AUTH_FAIL:EmptyUserId");
                Context.WebSocket.Close();
                return;
            }

            var tokenNew = SessionManager.GenerateSession(userId);
            SessionToken = tokenNew;
            Send($"AUTH_SUCCESS:{tokenNew}");
            Console.WriteLine($"[AuthServer] Authenticated user '{userId}' with token '{tokenNew}'");
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Console.WriteLine($"[AuthServer] WebSocket closed. Session {SessionToken} remains until timeout.");
            base.OnClose(e);
        }
    }
}
