using Discord.Commands;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;
using Fastenshtein;
using FuzzySharp;
using Council.DiscordBot.Core;
using Amazon.SQS;
using Amazon.Translate.Model;
using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using Amazon.Translate;
using Amazon.Comprehend;
using Amazon.Comprehend.Model;
using System.Collections.Generic;

[DiscordCommand]
public class ModerationModule : ModuleBase<SocketCommandContext>
{
    private readonly string[] _offenseTypes = { "Tile Hitting", "Banner Burning", "Base Attacking" };

    [Command("strike")]

    public async Task StrikeAsync([Remainder] string messageDetails)
    {
        bool finished = false;
        var bucketName = Environment.GetEnvironmentVariable("EVIDENCE_BUCKET");

        // Language detection and translation logic
        string detectedLanguageCode = DetectLanguage(messageDetails);
        if (detectedLanguageCode != "en") // Assuming English is the bot's primary language
        {
            messageDetails = TranslateText(messageDetails, detectedLanguageCode, "en");
        }

        // Evidence uploading logic
        List<string> evidenceS3Urls = new List<string>();
        if (Context.Message.Attachments.Any())
        {
            foreach (var attachment in Context.Message.Attachments)
            {
                var s3Url = await UploadToS3(bucketName, attachment.Url);
                evidenceS3Urls.Add(s3Url);
            }
        }

        // Extract Player ID
        var playerIdMatch = Regex.Match(messageDetails, @"\b\d{8,9}\b");
        if (!playerIdMatch.Success)
        {
            //translate if necessary
            await ReplyInSourceLanguage(detectedLanguageCode, "Could not find a valid Player ID. Please include an 8 or 9-digit Player ID.");
            return;
        }

        // Extract Player Name
        var playerNameMatch = Regex.Match(messageDetails, @"'([^']*)'");
        if (!playerNameMatch.Success)
        {
            //translate if necessary
            await ReplyInSourceLanguage(detectedLanguageCode, "Could not find a player name. Please enclose the player name in single quotes.");
            return;
        }

        // Extract Alliance
        var allianceMatch = Regex.Match(messageDetails, @"\[\w{3}\]");
        if (!allianceMatch.Success)
        {
            //translate if necessary
            await ReplyInSourceLanguage(detectedLanguageCode, "Could not find an alliance tag. Please include the alliance in the format [AAA].");
            return;
        }

        // Extract Offense Type and apply fuzzy logic to match it against known types
        var offenseTypeInput = messageDetails.Split(' ').LastOrDefault();
        var closestOffenseType = GetClosestOffenseType(offenseTypeInput);

        if (string.IsNullOrEmpty(closestOffenseType))
        {
            await ReplyInSourceLanguage(detectedLanguageCode, "The offense type is not recognized. Please provide a valid offense type.");
            return;
        }

        if (!evidenceS3Urls.Any())
        {
            await ReplyAsync("Please provide evidence for the offense (links or attach files).");
        }


        // Once evidence is provided, save all the collected data
        //SaveStrikeInformation(playerIdMatch.Value, playerNameMatch.Groups[1].Value, allianceMatch.Value, closestOffenseType, evidence);

        await ReplyAsync("The offense has been registered. Thank you.");
    }

    private async Task ReplyInSourceLanguage(string detectedLanguageCode, string englishResponse)
    {

        if (detectedLanguageCode == "en")
        {
            await ReplyAsync(englishResponse);
        }
        else
        {
            await ReplyAsync(TranslateText(englishResponse, "en", detectedLanguageCode));
        }

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

    private string DetectLanguage(string text)
    {
        using var cliient = new AmazonComprehendClient();
        var detectLanguageRequest = new DetectDominantLanguageRequest
        {
            Text = text
        };
        var detectLanguageResponse = cliient.DetectDominantLanguageAsync(detectLanguageRequest).Result;
        return detectLanguageResponse.Languages.OrderByDescending(l => l.Score).FirstOrDefault()?.LanguageCode;
    }

    private string TranslateText(string text, string sourceLanguageCode, string targetLanguageCode)
    {
        using var client = new AmazonTranslateClient();
        var translateRequest = new TranslateTextRequest
        {
            Text = text,
            SourceLanguageCode = sourceLanguageCode,
            TargetLanguageCode = targetLanguageCode
        };
        var translateResponse = client.TranslateTextAsync(translateRequest).Result;
        return translateResponse.TranslatedText;
    }

    private async Task<string> UploadToS3(string bucketName, string fileUrl)
    {
        using var client = new AmazonS3Client();
        var fileTransferUtility = new TransferUtility(client);

        // Assuming fileUrl is a direct URL to the file
        var fileName = fileUrl.Split('/').Last();
        var uploadRequest = new TransferUtilityUploadRequest
        {
            BucketName = bucketName,
            FilePath = fileUrl,
            Key = fileName
        };

        await fileTransferUtility.UploadAsync(uploadRequest);
        return $"https://{bucketName}.s3.amazonaws.com/{fileName}";
    }
}
