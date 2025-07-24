using System;
using System.Net;
using System.Threading.Tasks;
using Serilog;

namespace ApiGateway
{
    public static partial class ApiGatewayServer
    {
        private static HttpListener? _listener;

        public static async Task StartAsync()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://62.68.75.23:8090/");
            _listener.Start();

            Log.Information("[ApiGatewayServer] HTTP API läuft auf http://62.68.75.23:8090/");

            _ = Task.Run(MaintainAuthServerConnection);
            _ = Task.Run(MaintainPlayerServerConnection);

            while (_listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();

                    if (context.Request.IsWebSocketRequest && context.Request.Url?.AbsolutePath == "/ws")
                    {
                        Log.Information("Neue WebSocket-Anfrage von {RemoteEndPoint}", context.Request.RemoteEndPoint);
                        _ = Task.Run(() => HandleWebSocketClientAsync(context));
                    }
                    else
                    {
                        Log.Information("Neue HTTP-Anfrage: {HttpMethod} {Url}", context.Request.HttpMethod, context.Request.Url);
                        _ = Task.Run(() => HandleHttpRequest(context));
                    }
                }
                catch (HttpListenerException ex)
                {
                    Log.Warning("HttpListener wurde gestoppt: {Message}", ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[ApiGatewayServer] HTTP Fehler");
                }
            }
        }
    }
}
