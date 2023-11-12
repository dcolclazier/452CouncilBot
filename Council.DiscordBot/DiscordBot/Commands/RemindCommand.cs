using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using DiscordBot.Core;

namespace Council.DiscordBot.Commands
{

    [DiscordCommand]
    public class RemindCommand : ModuleBase<SocketCommandContext>
    {

        [Command("remind")]
        [Alias("r")]
        [Summary("Remind you of something after a given time (in minutes)")]
        public async Task RemindAsync(string reminder, int time)
        {
            
            var user = Context.Message.Author;
            if (time > 60 || time < 0)
            {
                await user.SendMessageAsync("Unfortunately, only reminders <= 60 minutes (and greater than 0) are supported currently.");
            }
            else
            {
                await ReplyAsync("Ok.");
                await Task.Delay(time * 60000);
                await user.SendMessageAsync($"REMINDER: {reminder}");
            }
        }

        [Command("remind")]
        [Alias("r")]
        [Summary("Remind you of something after a given time (in minutes)")]
        public async Task RemindAsync()
        {
            await ReplyAsync("To set a reminder, try this command: <!remind \"Remind me to take out the trash!\" 45> - the time is in minutes, and timers up to 1 hour are supported.");
        }
    }
}
