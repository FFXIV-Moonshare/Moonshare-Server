using System.Collections.Concurrent;

public static class EventLogManager
{
    private static readonly ConcurrentQueue<string> _events = new();
    private const int MaxEvents = 200;

    public static void LogInfo(string message)
        => Log($"[INFO] {message}");

    public static void LogError(string message)
        => Log($"[ERROR] {message}");

    public static void LogDebug(string message)
        => Log($"[DEBUG] {message}");

    private static void Log(string message)
    {
        var formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.WriteLine(formatted);

        if (_events.Count >= MaxEvents)
            _events.TryDequeue(out _);

        _events.Enqueue(formatted);
    }

    public static List<string> GetRecentEvents()
        => _events.ToList();
}
