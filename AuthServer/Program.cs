using System;
using System.Threading.Tasks;
using Serilog;
using Moonshare.Server.Server;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/authserver.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            Log.Information("🔌 AuthServer wird gestartet...");
            await AuthServer.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "❌ Unerwarteter Fehler beim Start.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
