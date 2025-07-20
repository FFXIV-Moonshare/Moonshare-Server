using System;
using WebSocketSharp;
using WebSocketSharp.Server;
using Moonshare.Server.Managers;

namespace Moonshare.Server.WebSocket
{
    public class SessionQueryBehavior : WebSocketBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.Data == "GET_SESSIONS")
            {
                var json = SessionManager.GetSessionsJson();
                Send(json);
                Console.WriteLine("[AuthServer] Sessions sent via WebSocket");
            }
            else
            {
                Send("ERROR:UnknownCommand");
            }
        }
    }
}
