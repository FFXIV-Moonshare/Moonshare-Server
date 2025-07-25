using Moonshare.Server.Managers;
using Moonshare.Server.Models;
using PlayerServer.Config;
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
        private readonly ServerConfig _config;
        private readonly WebSocketServer _wssv;
        private WebSocketSharp.WebSocket? _authServerSocket;
        private Timer? _sessionUpdateTimer;
        private Timer? _serverInfoTimer;
        private readonly SemaphoreSlim _updateLock = new(1, 1);
        private readonly ConcurrentDictionary<string, AuthSession> _localSessions = new();
        private readonly DateTime _startTime;
        private WebSocketSharp.WebSocket? _gatewaySocket;

        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentBag<Task> _listenerTasks = new();
        private readonly SemaphoreSlim _workerSemaphore;

        // Konstruktor
        public PlayerServerInstance(ServerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            string wsAddress = $"ws://{_config.IP}:{_config.Port}";
            _wssv = new WebSocketServer(wsAddress);
            _wssv.AddWebSocketService<PlayerBehavior>("/player", () => new PlayerBehavior(this, _config.Id));

            _startTime = DateTime.UtcNow;

            // Worker-Semaphore mit konfigurierten WorkerThreads
            _workerSemaphore = new SemaphoreSlim(_config.WorkerThreads);

            ConnectToGateway();
        }

        public int InstanceId => _config.Id;
        public int TotalInstances => _config.ShardCount;
        public string InstanceName => _config.Name;

        public bool SessionExists(string sessionToken)
            => _localSessions.ContainsKey(sessionToken);

        public void Start()
        {
            _wssv.Start();
            Log.Information("🎮 PlayerServer instance {InstanceId} running on {Address}/player", InstanceId, _wssv.Address);

            // Starte Listener-Threads (Tasks)
            for (int i = 0; i < _config.ListenerThreads; i++)
            {
                var task = Task.Run(ListenerLoopAsync, _cts.Token);
                _listenerTasks.Add(task);
            }

            StartDummySessionGenerator();
            ConnectAndStartSessionUpdates();
            StartPeriodicServerInfoUpdates();
        }

        public void Stop()
        {
            _cts.Cancel();

            Task.WaitAll(_listenerTasks.ToArray(), TimeSpan.FromSeconds(5));

            _sessionUpdateTimer?.Dispose();
            _serverInfoTimer?.Dispose();
            _authServerSocket?.Close();
            _gatewaySocket?.Close();
            _wssv.Stop();

            _workerSemaphore.Dispose();
            _cts.Dispose();
        }

        // Simulierte Listener-Task: z.B. Warteschlangenverarbeitung
        private async Task ListenerLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await ProcessWorkAsync();

                    await Task.Delay(100, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Listener Thread Exception in Instance {InstanceId}", InstanceId);
                }
            }
        }

        
        private async Task ProcessWorkAsync() //TODO
        {
            await _workerSemaphore.WaitAsync();

            try
            {
                

                await Task.Delay(50); 
            }
            finally
            {
                _workerSemaphore.Release();
            }
        }

        private void StartDummySessionGenerator()
        {
            _ = Task.Run(async () =>
            {
                int counter = 0;
                while (!_cts.IsCancellationRequested)
                {
                    if (_localSessions.Count >= _config.MaxDummySessions)
                    {
                        Log.Warning("Instance {InstanceId}: Dummy session limit {Limit} reached. No more sessions generated.", InstanceId, _config.MaxDummySessions);
                        break;
                    }

                    string sessionToken = $"DUMMY-{InstanceId}-{Guid.NewGuid()}";

                    var dummySession = new AuthSession
                    {
                        SessionToken = sessionToken,
                        UserId = $"user_{InstanceId}_{counter}",
                        CreatedAt = DateTime.UtcNow
                    };

                    _localSessions[sessionToken] = dummySession;
                    SessionManager.AddSession(InstanceId, sessionToken, dummySession);

                    counter++;

                    if (counter % 500 == 0)
                        Log.Information("Instance {InstanceId}: {Count} DummySessions created.", InstanceId, counter);

                    await Task.Delay(1);
                }

                Log.Information("Instance {InstanceId}: DummySession generator stopped at {Count} sessions.", InstanceId, _localSessions.Count);
            });
        }

        private void ConnectAndStartSessionUpdates()
        {
            _authServerSocket = new WebSocketSharp.WebSocket(_config.AuthServerWebSocketUrl);

            _authServerSocket.OnOpen += (_, _) =>
            {
                Log.Information("Instance {InstanceId}: Connected to AuthServer /sessions", InstanceId);
                RequestSessions();
                StartSessionUpdateTimer();
            };

            _authServerSocket.OnMessage += async (_, e) =>
            {
                await UpdateSessionsAsync(e.Data);
            };

            _authServerSocket.OnError += (_, e) =>
            {
                Log.Error("Instance {InstanceId}: WebSocket error: {Message}", InstanceId, e.Message);
            };

            _authServerSocket.OnClose += async (_, _) =>
            {
                Log.Error("Instance {InstanceId}: AuthServer disconnected. Reconnecting in 5s...", InstanceId);
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
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(_config.SessionUpdateIntervalSeconds));
        }

        private async Task UpdateSessionsAsync(string json)
        {
            await _updateLock.WaitAsync();
            try
            {
                var allSessions = JsonSerializer.Deserialize<AuthSession[]>(json);
                if (allSessions == null) return;

                var filteredSessions = allSessions
                    .Where(s => (GetStableHash(s.SessionToken) % TotalInstances) == InstanceId)
                    .ToArray();

                var currentTokens = filteredSessions.Select(s => s.SessionToken).ToHashSet();

                // Entferne alte Sessions
                var tokensToRemove = _localSessions.Keys.Where(t => !currentTokens.Contains(t)).ToList();
                foreach (var t in tokensToRemove)
                {
                    _localSessions.TryRemove(t, out _);
                    SessionManager.RemoveSession(InstanceId, t);
                }

                // Neue und aktualisierte Sessions hinzufügen
                foreach (var s in filteredSessions)
                {
                    bool added = SessionManager.AddSession(InstanceId, s.SessionToken, s);
                    if (added)
                        _localSessions[s.SessionToken] = s;
                    else
                    {
                        _localSessions.TryRemove(s.SessionToken, out _);
                        Log.Information("Instance {InstanceId}: Session {Token} not added (MaxPlayers reached), queued centrally.", InstanceId, s.SessionToken);
                    }
                }

                Log.Information("Instance {InstanceId}: {FilteredCount} sessions updated (total received: {TotalCount}), locally stored: {LocalCount}",
                    InstanceId, filteredSessions.Length, allSessions.Length, _localSessions.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Instance {InstanceId}: Error updating sessions", InstanceId);
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
                message = $"You are connected through instance {InstanceName}"
            };
            string json = JsonSerializer.Serialize(welcomeMessage);
            clientSocket.Send(json);
            Log.Information("Instance {InstanceName}: Sent connection info to client.", InstanceName);
        }

        private static (int totalPlayers, int maxPlayers) GetServerInfoAggregated()
        {
            int totalPlayers = 0;

            foreach (var sessions in SessionManager.ActiveSessions.Values)
                totalPlayers += sessions.Count;

            int maxPlayers = 10000; // statischer MaxPlayers Wert

            return (totalPlayers, maxPlayers);
        }

        private void ConnectToGateway()
        {
            _gatewaySocket = new WebSocketSharp.WebSocket(_config.GatewayWebSocketUrl);

            _gatewaySocket.OnOpen += (s, e) =>
            {
                Log.Information("Instance {InstanceId}: Connected to ApiGatewayServer WebSocket", InstanceId);
                SendServerInfoToGateway();
            };

            _gatewaySocket.OnClose += (s, e) =>
            {
                Log.Warning("Instance {InstanceId}: ApiGatewayServer WebSocket connection closed. Reconnect in 5s...", InstanceId);
                _gatewaySocket = null;
                Task.Delay(5000).ContinueWith(_ => ConnectToGateway());
            };

            _gatewaySocket.OnError += (s, e) =>
            {
                Log.Error("Instance {InstanceId}: ApiGatewayServer WebSocket error: {Message}", InstanceId, e.Message);
            };

            _gatewaySocket.Connect();
        }

        public void SendServerInfoToGateway()
        {
            if (_gatewaySocket == null || _gatewaySocket.ReadyState != WebSocketState.Open)
                return;

            var (totalPlayers, maxPlayers) = GetServerInfoAggregated();

            var uptime = DateTime.UtcNow - _startTime;
            int uptimeSeconds = (int)uptime.TotalSeconds;

            var status = totalPlayers > 0 ? "on" : "off";

            var data = new
            {
                name = InstanceName,
                players_online = totalPlayers,
                total_players_online = totalPlayers,
                max_players = maxPlayers,
                server_version = "0.1",
                uptime_seconds = uptimeSeconds,
                status = status
            };

            string jsonData = JsonSerializer.Serialize(data);
            string fullMessage = $"server_info:{jsonData}";
            _gatewaySocket.Send(fullMessage);

            Log.Information("Instance {InstanceId}: ServerInfo sent. Players global (ShardSessions)={Players} Uptime={Uptime}s Status={Status}",
                InstanceId, totalPlayers, uptimeSeconds, status);
        }

        private void StartPeriodicServerInfoUpdates()
        {
            _serverInfoTimer = new Timer(_ =>
            {
                SendServerInfoToGateway();
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(_config.ServerInfoUpdateIntervalSeconds));
        }

        public bool TryAddPlayer(string connectionId, PlayerBehavior player)
        {
            bool result = SessionManager.AddConnectedPlayer(InstanceId, connectionId, player);
            if (!result)
            {
                Log.Information("Instance {InstanceId}: MaxPlayers reached, player {UserId} queued (Queue handled centrally)", InstanceId, player.UserId);
            }
            return result;
        }

        public void RemovePlayer(string connectionId, string userId)
        {
            SessionManager.RemoveConnectedPlayer(InstanceId, connectionId);
        }
    }
}
