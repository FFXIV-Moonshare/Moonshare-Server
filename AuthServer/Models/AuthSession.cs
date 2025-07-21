    using System;

    namespace Moonshare.Server.Models
    {
        public class AuthSession
        {
            public string UserId { get; set; } = string.Empty;
            public string SessionToken { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public string ClientAddress { get; set; } = string.Empty; 
            public bool IsActive { get; set; } = true; 
        }

    }
