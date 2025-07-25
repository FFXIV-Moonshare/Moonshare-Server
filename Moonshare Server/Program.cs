using Moonshare.Server.Managers;
using PlayerServer.Config;
using Moonshare.Server.WebSocketHandlers;
using PlayerServer.Config;
using Serilog;
using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/server.log", rollingInterval: RollingInterval.Day)
            .MinimumLevel.Information()
            .CreateLogger();

        Log.Information("Lade Konfiguration...");

        ServerConfig config;
        try
        {
            if (!File.Exists("config.json"))
            {
                Log.Warning("config.json nicht gefunden. Erstelle Standardkonfiguration...");

                // Standard-Config erzeugen
                config = new ServerConfig();

                // Als JSON schön formatiert abspeichern
                var defaultJson = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText("config.json", defaultJson);

                Log.Information("Standardkonfiguration in config.json geschrieben.");
            }
            else
            {
                var json = File.ReadAllText("config.json");
                config = JsonSerializer.Deserialize<ServerConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new Exception("Config konnte nicht geladen werden.");
            }
        }
        catch (Exception ex)
        {
            Log.Fatal("Fehler beim Laden oder Erstellen der config.json: {Error}", ex.Message);
            return;
        }

        Log.Information("Starte PlayerServer-Instanzen ({Name}, Limit: {PlayerLimit}, Shards: {Shards})",
            config.Name, config.PlayerLimit, config.ShardCount);

        await PlayerServerManager.StartMultipleAsync(config);

        StartHttpCommandInterface();
        StartConsoleCommandInterface();
        AdminCommandHandler.LogAvailableCommandsOnStartup();

        Log.Information("Drücke Enter zum Beenden...");
        Console.ReadLine();

        PlayerServerManager.StopAll();
        Log.Information("Server wurde beendet.");
    }

    private static void StartHttpCommandInterface()
    {
        HttpListener listener = new();
        listener.Prefixes.Add("http://localhost:5050/");
        listener.Start();
        Log.Information("HTTP Admin Interface gestartet auf Port 5050");

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    var response = context.Response;
                    var url = context.Request.RawUrl?.Trim('/').ToLower() ?? "";

                    string result = AdminCommandHandler.Handle(url);
                    var buffer = System.Text.Encoding.UTF8.GetBytes(result);

                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                    response.Close();

                    Log.Information("Admin-Command via HTTP: {Url}", url);
                }
                catch (Exception ex)
                {
                    Log.Error("Fehler in HTTP Admin Interface: {Error}", ex.Message);
                }
            }
        });
    }

    private static void StartConsoleCommandInterface()
    {
        _ = Task.Run(() =>
        {
            while (true)
            {
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                try
                {
                    string result = AdminCommandHandler.Handle(input.Trim());
                    Console.WriteLine($"[Command Result] {result}");
                    Log.Information("Admin-Command via Console: {Command}", input);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Command Error] {ex.Message}");
                    Log.Error("Fehler bei Konsolenbefehl: {Error}", ex.Message);
                }
            }
        });
    }
}
