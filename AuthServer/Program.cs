using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Moonshare_Plugin
{
    public class UserSessionManager : IDisposable
    {
        private ClientWebSocket? playerSocket;
        private CancellationTokenSource? cts;

        public string? LocalUserId { get; private set; }
        public string? SessionToken { get; private set; }
        public string? ConnectedToUserId { get; private set; }

        public bool IsConnected => playerSocket?.State == System.Net.WebSockets.WebSocketState.Open;

        public async Task InitializeAsync()
        {
            try
            {
                string userId = "moonshare_user"; // Oder aus Config laden

                // 1) Authentifiziere via HTTP und hole Token vom AuthServer
                var authToken = await GetAuthTokenFromHttpAsync(userId);
                if (authToken == null)
                {
                    Console.WriteLine("❌ Authentifizierung fehlgeschlagen. Kein Token erhalten.");
                    return;
                }

                LocalUserId = userId;
                SessionToken = authToken;
                Console.WriteLine($"✅ Authentifiziert als {userId} mit Token: {authToken}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler bei Authentifizierung: {ex}");
                return;
            }

            // 2) Verbinde dich mit PlayerServer
            cts?.Cancel();
            cts = new CancellationTokenSource();
            playerSocket?.Dispose();
            playerSocket = new ClientWebSocket();

            try
            {
                var playerUri = new Uri("ws://localhost:5002/player");
                Console.WriteLine("🌐 Starte Verbindung zum PlayerServer...");
                await playerSocket.ConnectAsync(playerUri, cts.Token);
                Console.WriteLine("✅ Verbunden mit PlayerServer.");

                // 3) Sende Session-Token an PlayerServer
                string authMsg = "SESSION:" + SessionToken;
                var authBytes = Encoding.UTF8.GetBytes(authMsg);
                await playerSocket.SendAsync(authBytes, WebSocketMessageType.Text, true, cts.Token);

                // 4) Starte den ReceiveLoop im Hintergrund
                _ = Task.Run(() => ReceiveLoop(cts.Token), cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Verbindung zum PlayerServer fehlgeschlagen: {ex}");
                playerSocket.Dispose();
                playerSocket = null;
                cts.Cancel();
            }
        }

        // ✅ Neue Authentifizierungsmethode via HTTP
        private async Task<string?> GetAuthTokenFromHttpAsync(string userId)
        {
            try
            {
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync($"http://localhost:5003/sessions?userId={userId}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ AuthServer HTTP-Fehler: {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("token", out var tokenElement))
                {
                    return tokenElement.GetString();
                }

                Console.WriteLine("❌ Kein 'token'-Feld in Antwort.");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler bei HTTP-Request an AuthServer: {ex}");
                return null;
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            var buffer = new byte[4096];

            try
            {
                while (playerSocket?.State == System.Net.WebSockets.WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await playerSocket.ReceiveAsync(buffer, token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("❌ Server hat Verbindung geschlossen.");
                        await playerSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                        break;
                    }

                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"⬇️ Empfangen vom PlayerServer: {msg}");

                    try
                    {
                        using var doc = JsonDocument.Parse(msg);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("type", out var typeElem))
                        {
                            var type = typeElem.GetString();

                            switch (type)
                            {
                                case "message":
                                    var fromId = root.GetProperty("fromUserId").GetString();
                                    var payload = root.GetProperty("payload").GetString();
                                    Console.WriteLine($"📩 Nachricht von {fromId}: {payload}");
                                    break;

                                case "register":
                                    var registeredUserId = root.GetProperty("userId").GetString();
                                    Console.WriteLine($"✅ Spieler registriert: {registeredUserId}");
                                    break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"⚠️ Fehler beim Parsen der Nachricht: {e}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("ℹ️ ReceiveLoop wurde abgebrochen.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"❌ Fehler im ReceiveLoop: {e}");
            }
            finally
            {
                Console.WriteLine("🔌 ReceiveLoop beendet.");
            }
        }

        public async Task ConnectToAsync(string otherUserId)
        {
            if (!IsConnected)
            {
                Console.WriteLine("⚠️ Nicht mit PlayerServer verbunden.");
                return;
            }

            var msg = new { type = "connect", targetUserId = otherUserId };
            var json = JsonSerializer.Serialize(msg);
            var bytes = Encoding.UTF8.GetBytes(json);

            try
            {
                await playerSocket!.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                ConnectedToUserId = otherUserId;
                Console.WriteLine($"🔗 Verbindung zu {otherUserId} wird versucht...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Senden der Verbindungsnachricht: {ex}");
            }
        }

        public class SessionQueryBehavior : WebSocketBehavior
        {
            protected override void OnMessage(MessageEventArgs e)
            {
                if (e.Data == "GET_SESSIONS")
                {
                    var allSessions = SessionManager.GetAllSessions(); // Diese Methode muss im SessionManager vorhanden sein.
                    var json = JsonSerializer.Serialize(allSessions);
                    Send(json);
                    Console.WriteLine($"[AuthServer] Sessiondaten an PlayerServer gesendet: {allSessions.Length} Sessions");
                }
                else
                {
                    Send("UNKNOWN_COMMAND");
                    Context.WebSocket.Close();
                }
            }
        }

        public async Task DisconnectAsync()
        {
            if (!IsConnected) return;

            var msg = new { type = "disconnect" };
            var json = JsonSerializer.Serialize(msg);
            var bytes = Encoding.UTF8.GetBytes(json);

            try
            {
                await playerSocket!.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                ConnectedToUserId = null;
                Console.WriteLine("⛔️ Verbindung getrennt.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Senden der Trennnachricht: {ex}");
            }
        }

        public void Dispose()
        {
            cts?.Cancel();
            playerSocket?.Dispose();
        }
    }
}
