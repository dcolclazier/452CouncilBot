using Discord.Commands;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;
using Council.DiscordBot.Core;
using Amazon.Translate.Model;
using Amazon.S3;
using Amazon.S3.Transfer;
using Amazon.Translate;
using Amazon.Comprehend;
using Amazon.Comprehend.Model;
using System.Collections.Generic;
using Discord.WebSocket;
using System.Threading;
using System.Net.Http;
using System.IO;
using Nest;
using Amazon.SQS.Model;
using Elasticsearch.Net;
using Elasticsearch.Net.Aws;
using Amazon.Runtime;
using Amazon;
using Discord;
using Amazon.S3.Model;
using System.Numerics;
using Newtonsoft.Json;
using System.Text;

[DiscordCommand]
public class ModerationModule : ModuleBase<SocketCommandContext>
{
    private readonly string[] _offenseTypes = { "Tile", "Banner", "Base", "Scout" };
    private ElasticClient _elasticClient;
    private readonly string _evidenceBucketName = Environment.GetEnvironmentVariable("EVIDENCE_BUCKET");
    private readonly string _esEndpoint = Environment.GetEnvironmentVariable("ES_ENDPOINT");

    public ModerationModule()
    {
        try
        {
            var httpConnection = new AwsHttpConnection(new Amazon.Extensions.NETCore.Setup.AWSOptions
            {
                Credentials = new InstanceProfileAWSCredentials(),
                Region = RegionEndpoint.USWest2
            });

            var pool = new SingleNodeConnectionPool(new Uri($"https://{_esEndpoint}"));

            var settings = new ConnectionSettings(pool, httpConnection)
                .DefaultIndex("players")
                .DisableDirectStreaming();
            _elasticClient = new ElasticClient(settings);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }

    [Command("strike")]
    public async Task StrikeAsync()
    {
        var evidenceS3Urls = new List<string>();

        var messageDetails = Context.Message.Content;

        string playerId = null;
        string playerName = null;
        string allianceTag = null;
        string offenseType = null;

        // Language detection and translation logic
        var description = PreprocessMessageForLanguageDetection(messageDetails).Trim();

        string languageCode = DetectLanguage(PreprocessMessageForLanguageDetection(description));
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

        var offenseTypeInput = Regex.Match(messageDetails, "\"[^\"]*\"");
        if (offenseTypeInput.Success) offenseType = GetClosestOffenseType(offenseTypeInput.Value);

        if (Context.Message.Attachments.Any())
        {
            foreach (var attachment in Context.Message.Attachments)
            {
                //var s3Url = await UploadToS3(bucketName, attachment.Url);
                evidenceS3Urls.Add(attachment.Url);
            }
        }
        // If any information is missing, start an interactive dialogue
        if (playerId == null || playerName == null || allianceTag == null || offenseType == null || !evidenceS3Urls.Any())
        {
            await ReplyInSourceAsync(languageCode, "Some information is missing. Let's go through the details step by step.");
        }

        // Interactive dialogue to fill in missing information, looks like shit...
        if (playerId == null)
        {
            await ReplyInSourceAsync(languageCode, "Please enter the Player ID:");
            playerId = await GetInteractiveResponseAsync(@"\b\d{8,9}\b");
        }
        if (playerName == null)
        {
            await ReplyInSourceAsync(languageCode, "Please enter the Player's name, enclosed in single quotes:");
            playerName = await GetInteractiveResponseAsync(@"'([^']*)'");
            playerName = playerName?.Trim('\'');
        }
        if (allianceTag == null)
        {
            await ReplyInSourceAsync(languageCode, "Please enter the Alliance tag in the format [AAA]:");
            allianceTag = await GetInteractiveResponseAsync(@"\[\w{3}\]");
        }
        if (offenseType == null)
        {
            await ReplyInSourceAsync(languageCode, $"Please enter the Offense Type (${string.Join(", ",_offenseTypes)}):");
            offenseType = await GetInteractiveResponseAsync(string.Join("|", _offenseTypes), true); // Using fuzzy logic
        }


        var message = evidenceS3Urls.Any()
            ? "Would you like to provide any more evidence? Just say 'no' to finish."
            : "Please provide evidence for the offense (links or attach files) or 'no' to cancel:";
        
        await ReplyInSourceAsync(languageCode, message);
        evidenceS3Urls.AddRange(await GetAttachmentResponseAsync());
        if (evidenceS3Urls.Count == 0)
        {
            await ReplyInSourceAsync(languageCode, $"No evidence was provided, so this report will not be generated. Tip - try again with '!strike {playerId} \'{playerName}\' {allianceTag} \"{offenseType}\" {description}' and attach evidence all in the same message!");
            return;
        }

        var playerRecord = await CreateOrUpdatePlayerRecordAsync(_elasticClient, playerId, playerName, allianceTag);
        if (string.IsNullOrEmpty(playerRecord.playerId))
        {
            await ReplyInSourceAsync(languageCode, "I couldn't create a player record - something went wrong. Please contact Barry!");
        }

        var fileUrls = await CopyDiscordAttachmentsToS3Async(Guid.NewGuid().ToString(), _evidenceBucketName, evidenceS3Urls);
        if(fileUrls.Count != evidenceS3Urls.Count)
        {
            await ReplyInSourceAsync(languageCode, "I couldn't copy evidence to backend storage - something went wrong. Please contact Barry!");
        }

        var incidentId = await CreateOffenseReportAsync(_elasticClient, playerName, allianceTag, playerId, offenseType, fileUrls, description);
        if (string.IsNullOrEmpty(incidentId))
        {
            await ReplyInSourceAsync(languageCode, "I couldn't upload the offense report - something went wrong. Please contact Barry!");
        }

        await UpdatePlayerOffenses(playerRecord.playerId, incidentId);


        await ReplyInSourceAsync(languageCode, "The offense has possibly been registered (WORK IN PROGRESS). Here's the overview:");
        await ReplyAsync($"{incidentId}: {allianceTag} {playerName} ({playerId}) committed {offenseType} and {evidenceS3Urls.Count} pics/videos were collected as evidence.");
        await ReplyAsync($"Description: {description}");
    }
    private string PreprocessMessageForLanguageDetection(string message)
    {
        message = Regex.Replace(message, @"!strike\s+", "", RegexOptions.IgnoreCase);
        message = Regex.Replace(message, @"\[\w+\]", "");
        message = Regex.Replace(message, @"'[^']*'", "");
        message = Regex.Replace(message, @"\b\d{8,9}\b", "");
        message = Regex.Replace(message, "\"[^\"]*\"", "");
        return message;
    }

    private async Task<IEnumerable<string>> GetAttachmentResponseAsync()
    {
        var response = await NextMessageAsync(TimeSpan.FromSeconds(30));
        if (response != null || !response.Content.ToLower().Contains("no"))
        {
            return response.Attachments.Select(s => s.Url);
        }
        else
        {
            await ReplyAsync("No attachments. Got it!");
        }
        return new List<string>();
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

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        async Task MessageReceivedHandler(SocketMessage message)
        {
            if (message.Author.Id == sourceUser.Id && message.Channel.Id == sourceChannel.Id)
            {
                completionSource.TrySetResult(message);
            }
        }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

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
    private async Task ReplyInSourceAsync(string detectedLanguageCode, string englishResponse)
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


    //not working currently...
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
        try
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
        catch (Exception)
        {
            //todo logging
            return text;
        }
    }

    private async Task<List<string>> CopyDiscordAttachmentsToS3Async(string incidentId, string bucketName, List<string> fileUrls)
    {
        var s3Urls = new List<string>();
        try
        {
            using var client = new AmazonS3Client();
            var fileTransferUtility = new TransferUtility(client);

            foreach (var fileUrl in fileUrls)
            {
                // Extract fileName from fileUrl
                var fileName = Path.GetFileName(new Uri(fileUrl).AbsolutePath);

                // Download the file from Discord
                byte[] fileData;
                using var httpClient = new HttpClient();
                fileData = await httpClient.GetByteArrayAsync(fileUrl);

                // Create the correct key with the incident_id prefix
                var key = $"{incidentId}/{fileName}";

                // Prepare the memory stream from the downloaded data
                using var memoryStream = new MemoryStream(fileData);
                var uploadRequest = new TransferUtilityUploadRequest
                {
                    BucketName = bucketName,
                    InputStream = memoryStream,
                    Key = key
                };
                await fileTransferUtility.UploadAsync(uploadRequest);

                // Add the URL to the uploaded file to the list
                s3Urls.Add($"https://{bucketName}.s3.amazonaws.com/{key}");
            }
        }
        catch (Exception ex)
        {
            // Implement logging
            Console.WriteLine(ex.Message + ex.StackTrace);
            await ReplyAsync("Gross... I just swallowed a bug. GET IT OUT OF ME! ");
            await ReplyAsync(ex.Message);
            // Optionally, handle partial success if some files were uploaded before an error occurred
        }

        return s3Urls;
    }

    private async Task<PlayerRecord> CreateOrUpdatePlayerRecordAsync(ElasticClient client, string playerId, string playerName, string allianceTag)
    {
        try
        {
            var searchResponse = await client.SearchAsync<PlayerRecord>(s => s
                .Query(q => q
                    .Term(t => t
                        .Field(f => f.playerId)
                        .Value(playerId)
                    )
                )
            );

            PlayerRecord playerRecord;

            if (searchResponse.Documents.Any())
            {
                // Player exists, update record
                playerRecord = searchResponse.Documents.First();
                if (!playerRecord.knownNames.Contains(playerName))
                {
                    playerRecord.knownNames.Add(playerName);
                }
                if (!playerRecord.knownAlliances.Contains(allianceTag))
                {
                    playerRecord.knownAlliances.Add(allianceTag);
                }
                playerRecord.playerName = playerName;
                playerRecord.playerAlliance = allianceTag;
            }
            else
            {
                // Create new player record
                playerRecord = new PlayerRecord
                {
                    playerId = playerId,
                    playerName = playerName,
                    knownNames = new List<string> { playerName },
                    playerAlliance = allianceTag,
                    knownAlliances = new List<string> { allianceTag },
                    offenseIds = new List<string>()
                };
            }

            var indexResponse = await client.IndexDocumentAsync(playerRecord);
            return playerRecord;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message + ex.StackTrace);
            await ReplyAsync("Gross... I just swallowed a bug. GET IT OUT OF ME! ");
            await ReplyAsync(ex.Message);
            return new PlayerRecord();
        }
    }
    private async Task<string> CreateOffenseReportAsync(ElasticClient client, string playerName, string allianceTag, string playerId, string offenseType, List<string> evidenceUrls, string description)
    {
        try
        {
            var offenseReport = new OffenseReport
            {
                playerId = playerId,
                playerName = playerName,
                playerAlliance = allianceTag,
                offenseType = offenseType,
                date = DateTime.UtcNow,
                evidenceUrls = evidenceUrls,
                reportDetails = description
            };

            var indexResponse = await client.IndexAsync(offenseReport, i => i.Index("offense_reports"));

            if (!indexResponse.IsValid)
            {
                throw new Exception("Failed to index offense report: " + indexResponse.DebugInformation);
            }

            offenseReport.reportId = indexResponse.Id;

            var updateResponse = await client.UpdateAsync<OffenseReport>(indexResponse.Id, u => u
                .Index("offense_reports")
                .Doc(offenseReport)
            );

            if (!updateResponse.IsValid)
            {
                throw new Exception("Failed to update offense report with offenseId: " + updateResponse.DebugInformation);
            }

            return indexResponse.Id; 
        }
        catch (Exception ex)
        {
            // Implement logging
            Console.WriteLine(ex.Message + ex.StackTrace);
            await ReplyAsync("Gross... I just swallowed a bug. GET IT OUT OF ME!");
            await ReplyAsync(ex.Message);
            return string.Empty;
        }
    }


    private async Task UpdatePlayerOffenses(string playerId, string offenseId)
    {
        var updateResponse = await _elasticClient.UpdateAsync<PlayerRecord, object>(playerId, u => u
            .Index("players")
            .Script(s => s
                .Source("ctx._source.offenseIds.add(params.offenseId)")
                .Params(p => p
                    .Add("offenseId", offenseId)
                )
            )
        );
    }
    [Command("player")]
    public async Task GetPlayerByIdAsync(int playerId)
    {
        // Fetch player information
        var playerResponse = await _elasticClient.SearchAsync<PlayerRecord>(s => s
            .Query(q => q
                .Term(t => t
                    .Field(f => f.playerId)
                    .Value(playerId)
                )
            )
        );

        if (!playerResponse.Documents.Any())
        {
            await ReplyAsync("No player found with that ID.");
            return;
        }

        var player = playerResponse.Documents.First();

        // Fetch offenses related to the player
        var offenseResponse = await _elasticClient.SearchAsync<OffenseReport>(s => s
            .Query(q => q
                .Terms(t => t
                    .Field(f => f.playerId)
                    .Terms(player.offenseIds)
                )
            )
        );

        var embed = BuildPlayerEmbed(player, offenseResponse.Documents);
        await ReplyAsync(embed: embed.Build());
    }

    [Command("player")]
    public async Task GetPlayerByNameAsync(string name)
    {
        try
        {
            var response = await _elasticClient.SearchAsync<PlayerRecord>(s => s
                .Size(1)
                .Query(q => q
                    .Match(m => m
                        .Field(f => f.playerName)
                        .Query(name)
                    )
                )
            );


            if (!response.Documents.Any())
            {
                await ReplyAsync("No players found with a name close to that.");
                return;
            }
            var player = response.Documents.First();
            // Fetch offenses related to the player
            var offenseResponse = await _elasticClient.SearchAsync<OffenseReport>(s => s
                .Query(q => q
                    .Match(m => m
                        .Field(f => f.playerId)
                        .Query(player.playerId)
                    )
                )
            );
            Console.WriteLine(JsonConvert.SerializeObject(offenseResponse, Formatting.Indented));
            var embed = BuildPlayerEmbed(player, offenseResponse.Documents);
            await ReplyAsync(embed: embed.Build());
        }
        catch (Exception ex)
        {
            // Implement logging
            Console.WriteLine(ex.Message + ex.StackTrace);
            await ReplyAsync("Gross... I just swallowed a bug. GET IT OUT OF ME!");
            await ReplyAsync(ex.Message);
        }
    }

    private EmbedBuilder BuildPlayerEmbed(PlayerRecord player, IEnumerable<OffenseReport> offenses)
    {

        // Add player details to the embed
        var embed = new EmbedBuilder
        {
            Title = $"Player Information: {player.playerAlliance} {player.playerName}  ({player.playerId})",
            Color = Color.Blue
        };
        // ... add other player fields as needed

        // Add offenses
        if (offenses.Any())
        {
            var incidents = new StringBuilder();
            incidents.AppendLine("Incidents:");
            foreach (var offense in offenses)
            {
                incidents.AppendLine($"{offense.date.ToString("MM/dd/yyyy")} - {offense.offenseType} - {offense.reportId}");
            }
            embed.AddField("Offenses", incidents.ToString(), false);
        }
        else
        {
            embed.AddField("Offenses", "No offenses recorded", false);
        }

        // Set other embed properties as needed
        embed.WithColor(Color.Blue); // Example color

        return embed;
    }

    [Command("offense")]
    public async Task GetOffenseReportByIdAsync(string reportId)
    {
        try
        {
            // Fetch offense report
            var response = await _elasticClient.SearchAsync<OffenseReport>(s => s
                .Query(q => q
                    .Term(t => t
                        .Field(f => f.reportId)
                        .Value(reportId)
                    )
                )
            );
            Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));

            if (!response.Documents.Any())
            {
                await ReplyAsync("No offense report found with that ID.");
                return;
            }

            var offenseReport = response.Documents.First();

            // Prepare the embed
            var embed = BuildOffenseReportEmbed(offenseReport);

            // Download evidence files and prepare attachments
            var attachments = new List<FileAttachment>();
            foreach (var url in offenseReport.evidenceUrls)
            {
                var file = await DownloadFileAsync(url);
                if (file != null)
                {
                    attachments.Add((FileAttachment)file);
                }
            }

            // Send the message with embed and attachments
            await Context.Channel.SendFilesAsync(attachments, embed: embed.Build());
        }
        catch (Exception ex)
        {
            // Implement logging
            Console.WriteLine(ex.Message + ex.StackTrace);
            await ReplyAsync("Gross... I just swallowed a bug. GET IT OUT OF ME!");
            await ReplyAsync(ex.Message);
        }
    }

    private async Task<FileAttachment?> DownloadFileAsync(string url)
    {
        try
        {
            var uri = new Uri(url);
            var bucketName = uri.Host.Split('.')[0];
            var key = uri.AbsolutePath.Substring(1); // Remove the leading '/'

            using (var client = new AmazonS3Client())
            {
                var request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = key
                };

                using (var response = await client.GetObjectAsync(request))
                {
                    var stream = new MemoryStream();
                    await response.ResponseStream.CopyToAsync(stream);
                    stream.Position = 0; // Reset stream position to the beginning
                    var fileName = Path.GetFileName(key);
                    return new FileAttachment(stream, fileName);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    private EmbedBuilder BuildOffenseReportEmbed(OffenseReport offense)
    {
        var embed = new EmbedBuilder
        {
            Title = $"Offense Report: {offense.reportId}",
            Color = Color.Red
        };

        embed.AddField("Player", $"{offense.playerAlliance} {offense.playerName} ({offense.playerId})", inline: true);
        embed.AddField("Incident ID", offense.reportId, inline: true);
        embed.AddField("Offense Type", offense.offenseType, inline: true);
        embed.AddField("Details", offense.reportDetails, inline: true);
        
        return embed;
    }


    public class PlayerRecord
    {
        public string playerId { get; set; }
        public string playerName { get; set; }
        public List<string> knownNames { get; set; }
        public string playerAlliance { get; set; }
        public List<string> knownAlliances { get; set; }
        public bool redFlag { get; set; }
        public string redFlagReason { get; set; }
        public List<string> offenseIds { get; set; }
    }

    public class OffenseReport
    {
        public string reportId { get; set; }
        public string playerId { get; set; }
        public string playerName { get; set; }
        public string playerAlliance { get; set; }
        public string offenseType { get; set; }
        public DateTime date { get; set; }
        public List<string> evidenceUrls { get; set; }
        public string reportDetails { get; set; }
    }
}
