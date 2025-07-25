using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using PlayerServer.Config;

namespace Moonshare.Server.WebSocketHandlers
{
    public static class PlayerServerManager
    {
        private static List<PlayerServerInstance> _instances = new();

        public static async Task StartMultipleAsync(ServerConfig config)
        {
            _instances = new List<PlayerServerInstance>();

            Log.Information("Starting {Count} PlayerServer instances on base IP {IP} and Port {Port}...", config.ShardCount, config.IP, config.Port);

            try
            {
                string[] defaultNames = new string[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" };

                for (int i = 0; i < config.ShardCount; i++)
                {
                    int port = config.Port + i;

                    string name = i < defaultNames.Length ? defaultNames[i] : $"Instance{i}";

                    var instanceConfig = new ServerConfig
                    {
                        Id = i,
                        Name = name,
                        IP = config.IP,
                        Port = port,
                        PlayerLimit = config.PlayerLimit,
                        MaxSessions = config.MaxSessions,
                        ListenerThreads = config.ListenerThreads,
                        WorkerThreads = config.WorkerThreads,
                        SaveInterval = config.SaveInterval,
                        BlockedCountries = config.BlockedCountries,
                        BlockedAddresses = config.BlockedAddresses,
                        SecurityLevel = config.SecurityLevel,
                        AuthAPI = config.AuthAPI,
                        ShardCount = config.ShardCount,  
                        AuthServerWebSocketUrl = config.AuthServerWebSocketUrl,
                        GatewayWebSocketUrl = config.GatewayWebSocketUrl,
                        SessionUpdateIntervalSeconds = config.SessionUpdateIntervalSeconds,
                        ServerInfoUpdateIntervalSeconds = config.ServerInfoUpdateIntervalSeconds,
                        MaxDummySessions = config.MaxDummySessions
                    };

                    var instance = new PlayerServerInstance(instanceConfig);
                     instance.Start();  // Falls StartAsync async ist, sonst Start()
                    _instances.Add(instance);

                    Log.Information("Started PlayerServer instance {Name} (Shard {Id}) on ws://{IP}:{Port}", name, i, config.IP, port);
                }

                Log.Information("Successfully started all {Count} PlayerServer instances.", config.ShardCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while starting PlayerServer instances");
                throw;
            }
        }

        public static void StopAll()
        {
            Log.Information("Stopping all PlayerServer instances...");

            try
            {
                foreach (var instance in _instances)
                {
                    instance.Stop();
                    Log.Information("Stopped PlayerServer instance {Name} (Shard {ShardId})", instance.InstanceName, instance.InstanceId);
                }

                Log.Information("All PlayerServer instances stopped.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while stopping PlayerServer instances");
                throw;
            }
        }
    }
}
