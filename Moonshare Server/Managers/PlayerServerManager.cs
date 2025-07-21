using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;

namespace Moonshare.Server.WebSocketHandlers
{
    public static class PlayerServerManager
    {
        private static List<PlayerServerInstance> _instances = new();

        public static Task StartMultipleAsync(int instanceCount)
        {
            _instances = new List<PlayerServerInstance>();

            Log.Information("Starting {Count} PlayerServer instances...", instanceCount);

            try
            {
                
                string[] instanceNames = new string[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" };

                for (int i = 0; i < instanceCount; i++)
                {
                    int port = 5000 + i;
                    string url = $"ws://0.0.0.0:{port}";

                    
                    string name = i < instanceNames.Length ? instanceNames[i] : $"Instance{i}";

                    var instance = new PlayerServerInstance(i, instanceCount, url, name);
                    instance.Start();
                    _instances.Add(instance);

                    Log.Information("Started PlayerServer instance {InstanceName} ({InstanceId}) on {Url}", name, i, url);
                }

                Log.Information("Successfully started {Count} PlayerServer instances.", instanceCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while starting PlayerServer instances");
                throw;
            }

            return Task.CompletedTask;
        }

        public static void StopAll()
        {
            Log.Information("Stopping all PlayerServer instances...");

            try
            {
                foreach (var instance in _instances)
                {
                    instance.Stop();
                    Log.Information("Stopped PlayerServer instance {InstanceId}", instance.GetHashCode());
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
