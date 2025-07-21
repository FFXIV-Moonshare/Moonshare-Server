using Moonshare.Server.Managers;
using Moonshare.Server.Models;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Moonshare.Server.WebSocketHandlers
{
    public class PlayerServerInstance
    {
        private readonly int _instanceId;
        private readonly int _totalInstances;
        private readonly WebSocketServer _wssv;
        private WebSocketSharp.WebSocket? _authServerSocket;
        private Timer? _sessionUpdateTimer;
        private readonly SemaphoreSlim _updateLock = new(1, 1);
        private readonly ConcurrentDictionary<string, AuthSession> _localSessions = new();
        private readonly string _instanceName;

        public PlayerServerInstance(int instanceId, int totalInstances, string url, string instanceName)
        {
            _instanceId = instanceId;
            _totalInstances = totalInstances;
            _instanceName = instanceName;
            _wssv = new WebSocketServer(url);
            _wssv.AddWebSocketService<PlayerBehavior>("/player", () => new PlayerBehavior(this));

        }

        public void Start()
        {
            _wssv.Start();
            Log.Information("🎮 PlayerServer instance {InstanceId} running on {Address}:{Port}/player",
                _instanceId, _wssv.Address, _wssv.Port);

            ConnectAndStartSessionUpdates();
        }

        public void Stop()
        {
            _sessionUpdateTimer?.Dispose();
            _authServerSocket?.Close();
            _wssv.Stop();
        }

        private void ConnectAndStartSessionUpdates()
        {
            _authServerSocket = new WebSocketSharp.WebSocket("ws://62.68.75.23:5004/sessions");

            _authServerSocket.OnOpen += (_, _) =>
            {
                Log.Information("Instance {InstanceId}: Connected to AuthServer /sessions", _instanceId);
                RequestSessions();
                StartSessionUpdateTimer();
            };

            _authServerSocket.OnMessage += async (_, e) =>
            {
                await UpdateSessionsAsync(e.Data);
            };

            _authServerSocket.OnError += (_, e) =>
            {
                Log.Error("Instance {InstanceId}: WebSocket error: {Message}", _instanceId, e.Message);
            };

            _authServerSocket.OnClose += async (_, _) =>
            {
                Log.Error("Instance {InstanceId}: AuthServer disconnected. Reconnecting in 5s...", _instanceId);
                _sessionUpdateTimer?.Dispose();
                await Task.Delay(5000);
                ConnectAndStartSessionUpdates();
            };

            _authServerSocket.Connect();
        }

        private void RequestSessions()
        {
            if (_authServerSocket?.ReadyState == WebSocketState.Open)
                _authServerSocket.Send("GET_SESSIONS");
        }

        private void StartSessionUpdateTimer()
        {
            _sessionUpdateTimer = new Timer(_ =>
            {
                RequestSessions();
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private async Task UpdateSessionsAsync(string json)
        {
            await _updateLock.WaitAsync();
            try
            {
                var allSessions = JsonSerializer.Deserialize<AuthSession[]>(json);
                if (allSessions == null) return;

                // Sharding: Nur Sessions behalten, deren Hash % totalInstances == instanceId
                var filteredSessions = allSessions
                    .Where(s => (GetStableHash(s.SessionToken) % _totalInstances) == _instanceId)
                    .ToArray();

                var currentTokens = filteredSessions.Select(s => s.SessionToken).ToHashSet();

                // Update lokale Sessions
                foreach (var s in filteredSessions)
                    _localSessions[s.SessionToken] = s;

                // Entferne Sessions, die nicht mehr vorhanden sind
                var tokensToRemove = _localSessions.Keys.Where(t => !currentTokens.Contains(t)).ToList();
                foreach (var t in tokensToRemove)
                    _localSessions.TryRemove(t, out _);

                // Synchronisiere mit globalem SessionManager
                lock (SessionManager.Sessions)
                {
                    foreach (var s in filteredSessions)
                        SessionManager.Sessions[s.SessionToken] = s;
                    foreach (var t in tokensToRemove)
                        SessionManager.Sessions.TryRemove(t, out _);
                }

                Log.Information("Instance {InstanceId}: {FilteredCount} sessions updated from AuthServer (total sessions: {TotalCount})",
                    _instanceId, filteredSessions.Length, allSessions.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Instance {InstanceId}: Error updating sessions", _instanceId);
            }
            finally
            {
                _updateLock.Release();
            }
        }

        private static int GetStableHash(string str)
        {
            unchecked
            {
                int hash = 23;
                foreach (var c in str)
                    hash = hash * 31 + c;
                return Math.Abs(hash);
            }
        }


        public void SendInstanceInfoToClient(WebSocketSharp.WebSocket clientSocket)
        {
            var welcomeMessage = new
            {
                type = "connection_info",
                message = $"You are connected through instance {_instanceName}"
            };
            string json = JsonSerializer.Serialize(welcomeMessage);
            clientSocket.Send(json);
            Log.Information("Instance {InstanceName}: Sent connection info to client.", _instanceName);
        }
    }
}
