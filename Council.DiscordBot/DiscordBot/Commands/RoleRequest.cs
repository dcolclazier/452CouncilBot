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
using Council.DiscordBot.Core;

namespace Council.DiscordBot.Commands
{
    [DiscordCommand]
    public class RoleRequest : ModuleBase<SocketCommandContext>
    {
        private const int MatchThreshold = 70;
        private readonly List<string> _freeRoles = new List<string> { "Casino Jack's", "Leanie Land" };

        [Command("role")]
        [Summary("Requests a role for the user.")]
        public async Task RequestRoleAsync([Remainder][Summary("The role to request")] string requestedRoleString)
        {
            if (string.IsNullOrWhiteSpace(requestedRoleString))
            {
                await ReplyWithRoleInstructions();
                return;
            }

            var matchedRole = FindBestMatchingRole(requestedRoleString);
            if (matchedRole == null)
            {
                await ReplyWithNoMatchFound(requestedRoleString);
                return;
            }

            await AssignRoleToUser(matchedRole);
        }

        private async Task ReplyWithRoleInstructions()
        {
            await ReplyAsync("Try !role ROLE_NAME");
            await ReplyAsync($"Role list: {string.Join(",", _freeRoles)}");
        }

        private string FindBestMatchingRole(string roleRequest)
        {
            var bestMatch = FindBestMatchAmongFreeRoles(roleRequest) ?? FindBestMatchAmongGuildRoles(roleRequest);
            return bestMatch != null && bestMatch.MatchQuality >= MatchThreshold ? bestMatch.Role : null;
        }

        private dynamic FindBestMatchAmongFreeRoles(string roleRequest)
        {
            return _freeRoles.Select(role => new { Role = role, MatchQuality = Fuzz.PartialRatio(role, roleRequest) })
                             .OrderByDescending(r => r.MatchQuality)
                             .FirstOrDefault();
        }

        private dynamic FindBestMatchAmongGuildRoles(string roleRequest)
        {
            return Context.Guild.Roles.Select(role => new { Role = role.Name, MatchQuality = Fuzz.PartialRatio(role.Name, roleRequest) })
                                      .OrderByDescending(r => r.MatchQuality)
                                      .FirstOrDefault();
        }

        private async Task ReplyWithNoMatchFound(string roleRequest)
        {
            await ReplyAsync($"Couldn't match actual role... requested: {roleRequest}");
            await ReplyAsync(string.Join(",", Context.Guild.Roles.Select(r => r.Name)));
        }

        private async Task AssignRoleToUser(string roleName)
        {
            try
            {
                var actualRole = Context.Guild.Roles.First(role => role.Name == roleName);
                await Context.Guild.GetUser(Context.User.Id).AddRoleAsync(actualRole);
                await ReplyAsync($"{Context.User.Mention} has been granted the \"{roleName}\" role.");
                await NotifyAdminsOfRoleAssignment(roleName);
            }
            catch (Exception ex)
            {
                await ReplyAsync("Couldn't add role to user!");
                await ReplyAsync(ex.Message);
            }
        }

        private async Task NotifyAdminsOfRoleAssignment(string roleName)
        {
            var adminChannel = Context.Guild.TextChannels.FirstOrDefault(channel => channel.Name.Equals("role-requests", StringComparison.InvariantCultureIgnoreCase));
            if (adminChannel == null)
            {
                await ReplyAsync("Role Request channel not found!");
                return;
            }

            var message = await adminChannel.SendMessageAsync($"{Context.User.Mention} is requesting the \"{roleName}\" role. React with 👍 to approve or 👎 to deny.");
            await message.AddReactionAsync(new Emoji("👍"));
            await message.AddReactionAsync(new Emoji("👎"));
        }

        [DiscordEventHandler("ReactionAdded")]
        public async Task ReactionAddedAsync(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction reaction)
        {
            if (reaction.User.Value.IsBot || !(await cacheableMessage.GetOrDownloadAsync() is IUserMessage message) || !IsRoleRequestMessage(message))
                return;

            var user = await GetUserWhoReacted(cacheableMessage, reaction);
            if (user == null)
                return;

            var roleName = ExtractRoleNameFromMessage(message);
            var role = FindRoleInGuild(roleName, reaction);
            await ProcessRoleAssignmentReaction(user, role, reaction, message);
        }

        private bool IsRoleRequestMessage(IUserMessage message)
        {
            return message.Content.Contains("is requesting the");
        }
        private async Task<SocketUser> GetUserWhoReacted(Cacheable<IUserMessage, ulong> cache, SocketReaction reaction)
        {
            var message = await cache.GetOrDownloadAsync();
            if (message == null)
                return null;

            var users = await message.GetReactionUsersAsync(reaction.Emote, 100).FlattenAsync();
            return users.ToList().FirstOrDefault(u => !u.IsBot) as SocketUser;
        }

        private string ExtractRoleNameFromMessage(IUserMessage message)
        {
            return message.Content.Split('"')[1];
        }

        private SocketRole FindRoleInGuild(string roleName, SocketReaction reaction)
        {
            return (reaction.Channel as SocketTextChannel)?.Guild.Roles.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.InvariantCultureIgnoreCase));
        }

        private async Task ProcessRoleAssignmentReaction(SocketUser user, SocketRole role, SocketReaction reaction, IUserMessage message)
        {
            if (role == null)
                return;

            if (reaction.Emote.Name == "👍")
            {
                await AssignRoleToUserInGuild(user, role, message);
            }
            else if (reaction.Emote.Name == "👎")
            {
                await DenyRoleRequest(user, role, message);
            }

            await message.RemoveAllReactionsAsync();
        }

        private async Task AssignRoleToUserInGuild(SocketUser user, SocketRole role, IUserMessage message)
        {
            await (user as IGuildUser)?.AddRoleAsync(role);
            await message.Channel.SendMessageAsync($"{user.Mention} has been granted the \"{role.Name}\" role.");
        }

        private async Task DenyRoleRequest(SocketUser user, SocketRole role, IUserMessage message)
        {
            await message.Channel.SendMessageAsync($"{user.Mention}'s request for the \"{role.Name}\" role has been denied.");
        }
    }
}
