using Discord.Commands;
using MVP.DiscordBot.Core;
using System.Threading.Tasks;

namespace MVP.DiscordBot.Commands
{
    [DiscordCommand]
    public class PingCommand : ModuleBase<SocketCommandContext>
    {

        [Command("ping")]
        [Alias("p")]
        [Summary("See if I'm responding to commands")]
        public async Task PingAsync()
        {
            await ReplyAsync("pong");
        }

    }
}
