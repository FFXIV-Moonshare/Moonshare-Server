using System;
using System.Threading.Tasks;
using ApiGateway; // Namespace mit ApiGatewayServer
using Serilog;

class Program
{
    static async Task Main(string[] args)
    {
        // Serilog konfigurieren
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            // Optional: Weitere Sinks, z.B. File, Seq, etc.
            .CreateLogger();

        try
        {
            Log.Information("Starte ApiGatewayServer...");
            await ApiGatewayServer.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fehler beim Start des ApiGatewayServers");
        }
        finally
        {
            Log.Information("Beende Anwendung.");
            Log.CloseAndFlush();
        }
    }
}
