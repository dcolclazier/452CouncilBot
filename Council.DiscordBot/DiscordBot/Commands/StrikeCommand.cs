using Discord.Commands;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;
using Fastenshtein;
using FuzzySharp;

public class ModerationModule : ModuleBase<SocketCommandContext>
{
    private readonly string[] _offenseTypes = { "Tile Hitting", "Banner Burning", "Base Attacking" };

    [Command("strike")]
    public async Task StrikeAsync([Remainder] string messageDetails)
    {
        // Extract Player ID
        var playerIdMatch = Regex.Match(messageDetails, @"\b\d{8,9}\b");
        if (!playerIdMatch.Success)
        {
            await ReplyAsync("Could not find a valid Player ID. Please include an 8 or 9-digit Player ID.");
            return;
        }

        // Extract Player Name
        var playerNameMatch = Regex.Match(messageDetails, @"'([^']*)'");
        if (!playerNameMatch.Success)
        {
            await ReplyAsync("Could not find a player name. Please enclose the player name in single quotes.");
            return;
        }

        // Extract Alliance
        var allianceMatch = Regex.Match(messageDetails, @"\[\w{3}\]");
        if (!allianceMatch.Success)
        {
            await ReplyAsync("Could not find an alliance tag. Please include the alliance in the format [AAA].");
            return;
        }

        // Extract Offense Type and apply fuzzy logic to match it against known types
        var offenseTypeInput = messageDetails.Split(' ').LastOrDefault();
        var closestOffenseType = GetClosestOffenseType(offenseTypeInput);

        if (string.IsNullOrEmpty(closestOffenseType))
        {
            await ReplyAsync("The offense type is not recognized. Please provide a valid offense type.");
            return;
        }

        // Prompt for evidence, assuming it needs to be provided in a follow-up message
        await ReplyAsync("Please provide evidence for the offense (links or attach files).");

        // Evidence collection logic goes here
        // ...

        // Once evidence is provided, save all the collected data
        // SaveStrikeInformation(playerIdMatch.Value, playerNameMatch.Groups[1].Value, allianceMatch.Value, closestOffenseType, evidence);

        await ReplyAsync("The offense has been registered. Thank you.");
    }

    private string GetClosestOffenseType(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var closestOffenseType = _offenseTypes
            .Select(ot => new { OffenseType = ot, Distance = Fastenshtein.Levenshtein.Distance(input.ToLowerInvariant(), ot.ToLowerInvariant()) })
            .OrderBy(x => x.Distance)
            .FirstOrDefault();

        // Define a threshold for the closest match if necessary, e.g., if distance is more than 3, reject.
        int threshold = 3;
        return closestOffenseType?.Distance <= threshold ? closestOffenseType.OffenseType : null;
    }
}
