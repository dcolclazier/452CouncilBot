using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using Discord.WebSocket;
using System.Linq;
using System;
using System.Collections.Generic;
using FuzzySharp;
using Council.DiscordBot.Core;

namespace Council.DiscordBot.Commands
{
    [DiscordCommand]
    public class RoleRequest : ModuleBase<SocketCommandContext>
    {
        [Command("requestrole")]
        [Summary("Requests a role for the user.")]
        public async Task RequestRoleAsync([Remainder][Summary("The role to request")] string requestedRoleString)
        {
            var availableRoles = new List<string> { "[MVP] Member", "Admin" }; // Customize this list as needed

            var matchedRole = availableRoles
                                .OrderByDescending(role => Fuzz.PartialRatio(role, requestedRoleString))
                                .First();
            if (matchedRole == null)
            {
                await ReplyAsync("Role not found!");
                await ReplyAsync($"Role list: {string.Join(",", Context.Guild.Roles)}");
                return;
            }

            var adminChannel = Context.Guild.TextChannels.FirstOrDefault(ch => ch.Name.Equals("role-requests", StringComparison.InvariantCultureIgnoreCase));
            if (adminChannel == null)
            {
                await ReplyAsync("Admin channel not found!");
                return;
            }

            var message = await adminChannel.SendMessageAsync($"{Context.User.Mention} is requesting the \"{matchedRole}\" role. React with ?? to approve or ?? to deny.");
            await message.AddReactionAsync(new Emoji("👍"));
            await message.AddReactionAsync(new Emoji("👎"));
        }
        public async Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> cache, ISocketMessageChannel channel, SocketReaction reaction)
        {
            // Check if the reaction is from a message sent by the bot to avoid self-react
            if (reaction.User.Value.IsBot)
                return;

            // Get the message the reaction was added to
            var message = await cache.GetOrDownloadAsync();
            if (message == null)
                return;

            var guild = (reaction.Channel as SocketTextChannel)?.Guild;
            if (guild == null)
                return;

            // TODO: Adjust your condition, currently checking if the message contains "is requesting the"
            if (message.Content.Contains("is requesting the"))
            {
                var users = await message.GetReactionUsersAsync(reaction.Emote, 100).Flatten().ToListAsync(); // Adjust the limit as needed
                var user = users.FirstOrDefault(u => !u.IsBot); // Get the first non-bot user who reacted
                if (user == null)
                    return;

                var roleName = message.Content.Split('"')[1]; // Getting the role name from the message
                var role = guild.Roles.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.InvariantCultureIgnoreCase));

                if (reaction.Emote.Name == "👍")
                {
                    await guild.GetUser(user.Id).AddRoleAsync(role);
                    await message.Channel.SendMessageAsync($"{user.Mention} has been granted the \"{roleName}\" role.");
                }
                else if (reaction.Emote.Name == "👎")
                {
                    await message.Channel.SendMessageAsync($"{user.Mention}'s request for the \"{roleName}\" role has been denied.");
                }

                await message.RemoveAllReactionsAsync();
            }
        }

    }
}
