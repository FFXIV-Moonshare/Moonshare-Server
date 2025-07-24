using System.Text.Json.Serialization;


    public class ServerInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("players_online")]
        public int PlayersOnline { get; set; }

        [JsonPropertyName("max_players")]
        public int MaxPlayers { get; set; }

        [JsonPropertyName("server_version")]
        public string? ServerVersion { get; set; }

        [JsonPropertyName("uptime_seconds")]
        public int UptimeSeconds { get; set; }
    }

