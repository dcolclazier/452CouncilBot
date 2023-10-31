using Council.DiscordBot.Core;
using Discord.Commands;
using System.Threading.Tasks;

namespace Council.DiscordBot.Commands
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
