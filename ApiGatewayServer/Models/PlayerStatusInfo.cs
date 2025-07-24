using System;

public class PlayerStatusInfo
{
    public DateTime LastUpdated { get; set; }
    public string RawData { get; set; } = "";
    public object? ParsedData { get; set; } = null;
}