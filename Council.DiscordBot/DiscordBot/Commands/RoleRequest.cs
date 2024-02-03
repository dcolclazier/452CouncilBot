using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using Discord.WebSocket;
using System.Linq;
using System;
using System.Collections.Generic;
using FuzzySharp;
using DiscordBot.Core;
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

            var matchedRole = await FindBestMatchingRoleAsync(requestedRoleString);
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
            await ReplyAsync($"Role list: {string.Join(",", Context.Guild.Roles.Select(r => r.Name))}");
        }

        private async Task<string> FindBestMatchingRoleAsync(string roleRequest)
        {

            var bestMatch = FindBestMatch(Context.Guild.Roles.Select(r => r.Name).ToList(), roleRequest);
            await ReplyAsync($"Debug: {roleRequest}, {MatchThreshold}, {bestMatch.MatchQuality}");
            return bestMatch != null && bestMatch.MatchQuality >= MatchThreshold ? bestMatch.Role : null;
        }

        private dynamic FindBestMatch(List<string>matchList, string item)
        {
            return matchList.Select(role => new { Role = role, MatchQuality = Fuzz.PartialRatio(role, item) })
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
                await ReplyAsync("Role Request channel not found! Contact your administrator, and tell them to create a 'role-requests' channel so they can approve role requests.");
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

            var userToUpdate = await GetUserWhoRequestedRole(cacheableMessage);
            var roleName = ExtractRoleNameFromMessage(message);
            var role = FindRoleInGuild(roleName, reaction);
            await ProcessRoleAssignmentReaction(userToUpdate, role, reaction, message);
        }

        private bool IsRoleRequestMessage(IUserMessage message)
        {
            return message.Content.Contains("is requesting the");
        }

        private async Task<SocketUser> GetUserWhoRequestedRole(Cacheable<IUserMessage, ulong> cache)
        {
            var message = await cache.GetOrDownloadAsync();
            if (message == null) return null;

            var user = message.Content.Split(" ")[0];

            return Context.Guild.Users.FirstOrDefault(u => u.Mention == user);
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
