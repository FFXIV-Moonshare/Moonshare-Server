using System;

public class AuthSession
{
    public string UserId { get; set; } = "";
    public string SessionToken { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
