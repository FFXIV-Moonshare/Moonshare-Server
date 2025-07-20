using Moonshare.Server.Server;

class Program
{
    static async Task Main()
    {
        await AuthServer.StartAsync();
    }
}
