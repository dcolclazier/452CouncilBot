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
using Discord.WebSocket;
using System.Threading;

[DiscordCommand]
public class ModerationModule : ModuleBase<SocketCommandContext>
{
    private readonly string[] _offenseTypes = { "Tile Hitting", "Banner Burning", "Base Attacking" };

    [Command("strike")]

    public async Task StrikeAsync([Remainder] string messageDetails)
    {
        var bucketName = Environment.GetEnvironmentVariable("EVIDENCE_BUCKET");
        var evidenceS3Urls = new List<string>();


        string playerId = null;
        string playerName = null;
        string allianceTag = null;
        string offenseType = null;

        // Language detection and translation logic
        string languageCode = DetectLanguage(messageDetails);
        if (languageCode != "en") // Assuming English is the bot's primary language
        {
            messageDetails = TranslateText(messageDetails, languageCode, "en");
        }

        // Attempt to extract information from the initial message
        var playerIdMatch = Regex.Match(messageDetails, @"\b\d{8,9}\b");
        if (playerIdMatch.Success) playerId = playerIdMatch.Value;

        var playerNameMatch = Regex.Match(messageDetails, @"'([^']*)'");
        if (playerNameMatch.Success) playerName = playerNameMatch.Groups[1].Value;

        var allianceMatch = Regex.Match(messageDetails, @"\[\w{3}\]");
        if (allianceMatch.Success) allianceTag = allianceMatch.Value;

        var offenseTypeInput = messageDetails.Split(' ').LastOrDefault();
        offenseType = GetClosestOffenseType(offenseTypeInput);

        if (Context.Message.Attachments.Any())
        {
            foreach (var attachment in Context.Message.Attachments)
            {
                var s3Url = await UploadToS3(bucketName, attachment.Url);
                evidenceS3Urls.Add(s3Url);
            }
        }
        // If any information is missing, start an interactive dialogue
        if (playerId == null || playerName == null || allianceTag == null || offenseType == null || !evidenceS3Urls.Any())
        {
            await ReplyInSourceLanguage(languageCode, "Some information is missing. Let's go through the details step by step.");
        }

        // Interactive dialogue to fill in missing information
        if (playerId == null)
        {
            await ReplyAsync("Please enter the Player ID:");
            playerId = await GetInteractiveResponseAsync(@"\b\d{8,9}\b");
        }
        if (playerName == null)
        {
            await ReplyAsync("Please enter the Player's name, enclosed in single quotes:");
            playerName = await GetInteractiveResponseAsync(@"'([^']*)'");
            playerName = playerName?.Trim('\'');
        }
        if (allianceTag == null)
        {
            await ReplyAsync("Please enter the Alliance tag in the format [AAA]:");
            allianceTag = await GetInteractiveResponseAsync(@"\[\w{3}\]");
        }
        if (offenseType == null)
        {
            await ReplyAsync("Please enter the Offense Type:");
            offenseType = await GetInteractiveResponseAsync(string.Join("|", _offenseTypes), true); // Using fuzzy logic
        }
        if (!evidenceS3Urls.Any())
        {
            await ReplyAsync("Please provide evidence for the offense (links or attach files):");
            // Logic for collecting and uploading evidence
        }


        // Once evidence is provided, save all the collected data
        //SaveStrikeInformation(playerIdMatch.Value, playerNameMatch.Groups[1].Value, allianceMatch.Value, closestOffenseType, evidence);

        await ReplyAsync("The offense has been registered. Thank you.");
        await ReplyAsync($"DEBUG: [{allianceTag}] {playerName} ({playerId}) committed {offenseType} and {evidenceS3Urls.Count} pics/videos were collected as evidence.");
    }
    private async Task<string> GetInteractiveResponseAsync(string pattern, bool isOffenseType = false)
    {
        var response = await NextMessageAsync();
        if (response != null)
        {
            if (isOffenseType)
            {
                // Special handling for offense type to include fuzzy logic matching
                return GetClosestOffenseType(response.Content);
            }
            else if (Regex.IsMatch(response.Content, pattern))
            {
                return Regex.Match(response.Content, pattern).Value;
            }
        }
        await ReplyAsync("Invalid input or no input received. Please try again or type 'cancel' to stop the operation.");
        return await GetInteractiveResponseAsync(pattern, isOffenseType);
    }
    private async Task<SocketMessage> NextMessageAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(15); // Default timeout
        var sourceUser = Context.User;
        var sourceChannel = Context.Channel;

        var completionSource = new TaskCompletionSource<SocketMessage>();

        // MessageReceived handler that completes the task when conditions are met
        async Task MessageReceivedHandler(SocketMessage message)
        {
            if (message.Author.Id == sourceUser.Id && message.Channel.Id == sourceChannel.Id)
            {
                completionSource.TrySetResult(message);
            }
        }

        // Register the handler to the MessageReceived event
        Context.Client.MessageReceived += MessageReceivedHandler;

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(timeout.Value);

        // Check if the task has completed or the timeout has been reached
        await Task.WhenAny(completionSource.Task, Task.Delay(timeout.Value, cancellationTokenSource.Token));

        // Unregister the handler from the MessageReceived event
        Context.Client.MessageReceived -= MessageReceivedHandler;

        // Return the result of the task or null if it's cancelled or timed out
        return completionSource.Task.IsCompletedSuccessfully ? completionSource.Task.Result : null;
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
