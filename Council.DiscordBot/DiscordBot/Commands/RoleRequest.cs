using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using Discord.WebSocket;
using System.Linq;
using System;
using System.Collections.Generic;
using FuzzySharp;
using DiscordBot.Core;
using Amazon.SQS.Model;
using static Nest.JoinField;
using System.Data;

namespace Council.DiscordBot.Commands
{
    [DiscordCommand]
    public class RoleRequest : ModuleBase<SocketCommandContext>
    {
        private readonly List<string> _freeRoles = new()
        {
            "Barry's Casino",
            "Leanie Land"
        };
        [Command("role")]
        [Summary("Requests a role for the user.")]
        public async Task RequestRoleAsync([Remainder][Summary("The role to request")] string requestedRoleString)
        {
            

            if (string.IsNullOrWhiteSpace(requestedRoleString))
            {
                await ReplyAsync("Try !role ROLE_NAME");
                await ReplyAsync($"Role list: {string.Join(",", _freeRoles)}");
                return;
            }

            var matchedRole = _freeRoles.MaxBy(role => Fuzz.PartialRatio(role, requestedRoleString));
            if (matchedRole == null)
            {
                await ReplyAsync("Role not found!");
                await ReplyAsync($"Role list: {string.Join(",", _freeRoles)}");
                return;
            }

            await ReplyAsync("Granting Role...");

            var actualRole = Context.Guild.Roles.MaxBy(role => Fuzz.PartialRatio(role.Name, matchedRole));
            if (actualRole == null)
            {
                await ReplyAsync("Couldn't match actual role!");
                await ReplyAsync(string.Join(",", Context.Guild.Roles.Select(r => r.Name)));
                return;
            }

            try
            {
                await Context.Guild.GetUser(Context.User.Id).AddRoleAsync(actualRole);
            }
            catch (Exception ex)
            {
                await ReplyAsync("Couldn't add role to user!");
                await ReplyAsync(ex.Message);
                return;
            }
            
            await ReplyAsync($"{Context.User.Mention} has been granted the \"{matchedRole}\" role.");
            //var adminChannel = Context.Guild.TextChannels.FirstOrDefault(ch => ch.Name.Equals("role-requests", StringComparison.InvariantCultureIgnoreCase));
            //if (adminChannel == null)
            //{
            //    await ReplyAsync("Admin channel not found!");
            //    return;
            //}

            //var message = await adminChannel.SendMessageAsync($"{Context.User.Mention} is requesting the \"{matchedRole}\" role. React with ?? to approve or ?? to deny.");
            //await message.AddReactionAsync(new Emoji("👍"));
            //await message.AddReactionAsync(new Emoji("👎"));
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
