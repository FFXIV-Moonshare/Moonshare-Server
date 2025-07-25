using System;
using System.Net;

namespace PlayerServer.Config
{
    public class ServerConfig
    {
        public string Name { get; set; } = "DefaultServer";
        public int Id { get; set; } = 1;

       
        public string IP { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 5000;

        public int ListenerThreads { get; set; } = 1;
        public int WorkerThreads { get; set; } = 2;

       
        public int PlayerLimit { get; set; } = 10000;
        public int MaxSessions { get; set; } = 10000;

      
        public int ShardCount { get; set; } = 1;

       
        public SecurityLevel SecurityLevel { get; set; } = SecurityLevel.User;

       
        public AuthApiSettings AuthAPI { get; set; } = new();

       
        public TimeSpan SaveInterval { get; set; } = TimeSpan.FromMinutes(1);

        
        public string[] BlockedCountries { get; set; } = Array.Empty<string>();
        public string[] BlockedAddresses { get; set; } = Array.Empty<string>();

      
        public string AuthServerWebSocketUrl { get; set; } = "ws://localhost:5004/sessions";
        public string GatewayWebSocketUrl { get; set; } = "ws://localhost:8090/ws";

        
        public int SessionUpdateIntervalSeconds { get; set; } = 30;

       
        public int ServerInfoUpdateIntervalSeconds { get; set; } = 30;

        // Max Dummy Sessions (für Test oder Simulation)
        public int MaxDummySessions { get; set; } = 500;

        // WebSocket-URL für den PlayerServer Listener (z.B. ws://0.0.0.0:28003)
        public string PlayerServerWebSocketUrl => $"ws://{IP}:{Port}";
    }

    public class AuthApiSettings
    {
        public string Url { get; set; } = "http://localhost:5000/sessions";
        public string ApiKey { get; set; } = "";
    }

    public enum SecurityLevel
    {
        Guest = 0,
        User = 1,
        Moderator = 2,
        Admin = 3
    }
}
