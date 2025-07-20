using System.Threading.Tasks;
using Moonshare.Server.WebSocketHandlers;

namespace Moonshare.Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await PlayerServer.StartAsync();
        }
    }
}
