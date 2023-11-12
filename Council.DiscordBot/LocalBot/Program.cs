using DiscordBot.Core;
using System.Threading.Tasks;

namespace LocalBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await new DiscordBotLocal().RunAsync();
        }
    }
}
