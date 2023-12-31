﻿using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Transfer;
using Council.DiscordBot;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Core;
using DiscordBot.Core.Contract;
using Elasticsearch.Net;
using Elasticsearch.Net.Aws;
using MEF.NetCore;
using Nest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


[DiscordCommand]
public class ModerationModule : ModuleBase<SocketCommandContext>
{
    private readonly string[] _offenseTypes = { "Tile", "Banner", "Base", "Scout" };
    private readonly ElasticClient _elasticClient;
    private readonly string _evidenceBucketName = Environment.GetEnvironmentVariable("EVIDENCE_BUCKET");
    private readonly string _esEndpoint = Environment.GetEnvironmentVariable("ES_ENDPOINT");
    [Import]
    private IS3Service _s3Service { get; set; }

    [Import]
    private ILanguageService Translator { get; set; }

    [Import]
    private IElasticsearchService ElasticClient { get; set; }

    public ModerationModule()
    {

        MEFLoader.SatisfyImportsOnce(this);
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
                .DisableDirectStreaming()
                .OnRequestCompleted(callDetails =>
                {
                    // Log the request
                    if (callDetails.RequestBodyInBytes != null)
                    {
                        var requestBody = Encoding.UTF8.GetString(callDetails.RequestBodyInBytes);
                        var prettyJsonRequest = JToken.Parse(requestBody).ToString(Formatting.Indented);
                        Console.WriteLine($"{callDetails.HttpMethod} {callDetails.Uri} \n{prettyJsonRequest}");
                    }

                    // Log the response
                    if (callDetails.ResponseBodyInBytes == null) return;
                    
                    var responseBody = Encoding.UTF8.GetString(callDetails.ResponseBodyInBytes);
                    var prettyJsonResponse = JToken.Parse(responseBody).ToString(Formatting.Indented);
                    Console.WriteLine($"Status: {callDetails.HttpStatusCode}\n{prettyJsonResponse}");
                });
            _elasticClient = new ElasticClient(settings);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }


    }
    public string ParseMessageContents(string messageDetails, string regex, bool isGroupRegex)
    {
        Match match;
        Console.WriteLine($"[DEBUG] ParseMessage: {messageDetails} | {regex} | {isGroupRegex}");
        if (isGroupRegex)
        {
            Console.WriteLine("Is Group Regex...");
            match = Regex.Match(messageDetails, regex, RegexOptions.IgnoreCase);
            var groupValue = match.Success ? match.Groups[1].Value : string.Empty;
            Console.WriteLine($"Match: {groupValue}");
            return groupValue;
        }
        Console.WriteLine("Is not group regex");
        match = Regex.Match(messageDetails, regex);

        var value = match.Success ? match.Value : string.Empty;
        Console.WriteLine($"Match: {value}");
        return value;
    }

    public async Task<string> GetResponseFromUser(string prompt, string messageDetails, string regex, string languageCode, bool isOffenseType = false)
    {
        var parsed = ParseMessageContents(messageDetails, regex, isOffenseType);
        Console.WriteLine($"GetResponse parsed message: {parsed}");
        if (!string.IsNullOrEmpty(parsed)) return parsed;

        await ReplyInSourceAsync(languageCode, $"{prompt} (or 'cancel'):");
        var response = await GetInteractiveResponseAsync(regex, isOffenseType);
        if (response.ToLower() == "cancel")
        {
            throw new OperationCanceledException("Operation was cancelled");
        }
        Console.WriteLine($"Retrieved response: {response}");
        return await GetResponseFromUser(prompt, response, regex, languageCode, isOffenseType);
    }

    [Command("strike")]
    public async Task StrikeAsync()
    {
        try
        {
            var evidenceS3Urls = new List<string>();

            var messageDetails = Context.Message.Content.Replace("!strike", "");

            // Language detection and translation logic
            var description = PreprocessMessageForLanguageDetection(messageDetails).Trim();
            var languageCode = string.IsNullOrEmpty(description) ? "en" : await Translator.DetectLanguageAsync(description);
            Console.WriteLine($"The detected language code is {languageCode}");
            if (languageCode != "en") 
            {
                messageDetails = await Translator.TranslateTextAsync(messageDetails, languageCode, "en");
            }

            Console.WriteLine($"Translated Message: {messageDetails}");
            if (Context.Message.Attachments.Any())
            {
                evidenceS3Urls.AddRange(Context.Message.Attachments.Select(attachment => attachment.Url));
            }
            Console.WriteLine($"Found {evidenceS3Urls.Count} initial attachments.");

            var playerId = await GetResponseFromUser(
                "Please enter the Player ID:", 
                messageDetails, @"\b\d{8,9}\b", languageCode);

            Console.WriteLine($"Determined player id: {playerId}");

            
            var playerName = await GetResponseFromUser(
                "Please enter the Player's name, enclosed in single quotes", 
                messageDetails, @"'([^']*)'", languageCode);
            Console.WriteLine($"Determined player name: {playerName}");

            var allianceTag = await GetResponseFromUser(
                "Please enter the Alliance tag in the format [AAA]",
                messageDetails, @"\[[A-Za-z]{3}\]", languageCode);
            Console.WriteLine($"Determined alliance tag: {allianceTag}");

            var offenseTypesPattern = string.Join("|", _offenseTypes.Select(Regex.Escape));
            Console.WriteLine($"Offense types pattern: {offenseTypesPattern}");
            var pattern = $@"\(({offenseTypesPattern})\)"; // Pattern to match (Base), (Tile), etc.
            Console.WriteLine($"Regex pattern: {pattern}");

            var offenseType = await GetResponseFromUser(
                $"Please enter the offense type surrounded by () ({string.Join(", ", _offenseTypes)})",
                messageDetails, pattern, languageCode, true);
            Console.WriteLine($"Determined offense type: {offenseType}");

            await ReplyInSourceAsync(languageCode, evidenceS3Urls.Any()
                ? "Would you like to provide any more evidence? Just say 'no' to finish."
                : "Please provide evidence for the offense (links or attach files) or 'no' to cancel:");

            evidenceS3Urls.AddRange(await GetAttachmentResponseAsync());
            if (evidenceS3Urls.Count == 0)
            {
                await ReplyInSourceAsync(languageCode, $"No evidence was provided, so this report will not be generated. Tip - try again with '!strike {playerId} \'{playerName}\' {allianceTag} \"{offenseType}\" {description}' and attach evidence all in the same message!");
                return;
            }

            var playerRecord = await CreateOrUpdatePlayerRecordAsync(_elasticClient, playerId, playerName, allianceTag);
            var fileUrls = await CopyDiscordAttachmentsToS3Async(Guid.NewGuid().ToString(), _evidenceBucketName, evidenceS3Urls);
            var incidentId = await CreateOffenseReportAsync(_elasticClient, playerName, allianceTag, playerId, offenseType, fileUrls, description);

            await UpdatePlayerOffenses(playerRecord.playerId, incidentId);

            var embed = new EmbedBuilder
            {
                Title = $"The offense has been registered.",
                Color = Color.Blue
            };
            embed.AddField("Report Id:", incidentId);
            embed.AddField("Player name:", $"{playerName} ({playerId})");
            embed.AddField("Offense Type:", offenseType);
            embed.AddField("Evidence pics/video count:", evidenceS3Urls.Count);
            embed.AddField("Description", description);
            embed.Footer = new EmbedFooterBuilder
            {
                Text = $"!offense {incidentId}"
            };

            await ReplyAsync(embed: embed.Build());

        }
        catch (OperationCanceledException)
        {
            await ReplyAsync("Operation has been cancelled.");
        }
        catch (Exception ex)
        {
            // Implement logging
            await ReplyAsync("Gross... I just swallowed a bug. GET IT OUT OF ME! ");
            await ReplyAsync(ex.ConcatMessages());
            // Optionally, handle partial success if some files were uploaded before an error occurred
        }
    }
    private static string PreprocessMessageForLanguageDetection(string message)
    {
        Console.WriteLine($"Message before preprocessing: {message}");
        message = Regex.Replace(message, @"!strike\s+", "", RegexOptions.IgnoreCase);
        message = Regex.Replace(message, @"\[\w+\]", "");
        message = Regex.Replace(message, @"'[^']*'", "");
        message = Regex.Replace(message, @"\b\d{8,9}\b", "");
        var isFirst = true;
        message = Regex.Replace(message, @"\(([^)]+)\)", m =>
        {
            if (!isFirst) return m.Value;
            isFirst = false;
            return "";
        });
        Console.WriteLine($"Message after preprocessing: {message}");

        return message;
    }

    private async Task<IEnumerable<string>> GetAttachmentResponseAsync()
    {
        var response = await NextMessageAsync(TimeSpan.FromSeconds(120));
        if (response != null && !response.Content.ToLower().Contains("no"))
        {
            return response.Attachments.Select(s => s.Url);
        }
        return new List<string>();
    }

    private async Task<string> GetInteractiveResponseAsync(string pattern, bool isOffenseType = false)
    {
        var response = await NextMessageAsync();
        if (response != null)
        {
            if(response.Content.ToLower().Trim() == "cancel")
            {
                return response.Content;
            }
            if (Regex.IsMatch(response.Content, pattern, RegexOptions.IgnoreCase))
            {
                return Regex.Match(response.Content, pattern).Value;
            }
        }
        await ReplyAsync("Invalid input or no input received. Please try again or type 'cancel' to stop the operation.");
        return await GetInteractiveResponseAsync(pattern, isOffenseType);
    }
    private async Task<SocketMessage> NextMessageAsync(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30); // Default timeout
        var sourceUser = Context.User;
        var sourceChannel = Context.Channel;

        var completionSource = new TaskCompletionSource<SocketMessage>();
        // Register the handler to the MessageReceived event
        Context.Client.MessageReceived += MessageReceivedHandler;

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(timeout.Value);

        // Check if the task has completed or the timeout has been reached
        await Task.WhenAny(completionSource.Task, Task.Delay(timeout.Value, cancellationTokenSource.Token));

        Context.Client.MessageReceived -= MessageReceivedHandler;

        // Return the result of the task or null if it's cancelled or timed out
        return completionSource.Task.IsCompletedSuccessfully ? completionSource.Task.Result : null;

        Task MessageReceivedHandler(SocketMessage message)
        {
            if (message.Author.Id == sourceUser.Id && message.Channel.Id == sourceChannel.Id)
            {
                completionSource.TrySetResult(message);
            }

            return Task.CompletedTask;
        }
    }
    private async Task ReplyInSourceAsync(string detectedLanguageCode, string englishResponse)
    {

        if (detectedLanguageCode == "en")
        {
            await ReplyAsync(englishResponse);
        }
        else
        {
            await ReplyAsync(await Translator.TranslateTextAsync(englishResponse, "en", detectedLanguageCode));
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
                using var httpClient = new HttpClient();
                var fileData = await httpClient.GetByteArrayAsync(fileUrl);

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
            Console.WriteLine($"Checking to see if player exists with id {playerId}");
            var searchResponse = await client.SearchAsync<PlayerRecord>(s => s
                .Query(q => q
                    .Term(t => t
                        .Field(f => f.playerId)
                        .Value(playerId)
                    )
                )
            );
            Console.WriteLine(searchResponse.ToJsonString(true));
            PlayerRecord playerRecord;


            if (searchResponse.Documents.Any())
            {
                Console.WriteLine($"Player exists - updating player record.");
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
                Console.WriteLine($"Player does not exist with id {playerId} - creating new record.");
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
            Console.WriteLine(indexResponse.ToJsonString(true));
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
        Console.WriteLine($"Updating player offenses for player id {playerId}");
        var searchResponse = await _elasticClient.SearchAsync<PlayerRecord>(s => s
            .Index("players")
            .Query(q => q
                .Term(t => t.Field(f => f.playerId).Value(playerId))
            )
            .Source(false)
            .Size(1)
        );

        if (searchResponse.Hits.Any())
        {
            var documentId = searchResponse.Hits.First().Id; // This is the _id of the document

            var updateResponse = await _elasticClient.UpdateAsync<PlayerRecord, object>(documentId, u => u
                .Index("players")
                .Script(s => s
                    .Source("ctx._source.offenseIds.add(params.offenseId)")
                    .Params(p => p.Add("offenseId", offenseId))
                )
            );
            if (!updateResponse.IsValid)
            {
                Console.WriteLine("Error updating document.");
            }
        }
        else
        {
            // Handle the case where no documents are found
            Console.WriteLine("No documents found with the provided playerId.");
        }

    }
    [Command("player")]
    public async Task GetPlayerByIdAsync(int playerId)
    {
        // Fetch player information
        var stringId = playerId.ToString();
        var searchResponse = await _elasticClient.SearchAsync<PlayerRecord>(s => s
            .Query(q => q
                .Term(t => t
                    .Field(f => f.playerId)
                    .Value(stringId)
                )
            )
        ); 
        Console.WriteLine($"Player Query Response: {searchResponse.ToJsonString(true)}");
        if (!searchResponse.Documents.Any())
        {
            await ReplyAsync("No player found with that ID.");
            return;
        }

        var player = searchResponse.Documents.First();

        // Fetch offenses related to the player
        var offenseResponse = await _elasticClient.SearchAsync<OffenseReport>(s => s
            .Index("offense_reports")
            .Query(q => q
                .Term(t => t
                    .Field(f => f.playerId)
                    .Value(player.playerId)
                )
            )
        );

        Console.WriteLine(JsonConvert.SerializeObject(offenseResponse, Formatting.Indented));


        await SendPlayerInfoAsync(player, offenseResponse.Documents);
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
                .Index("offense_reports")
                .Query(q => q
                    .Match(m => m
                        .Field(f => f.playerId)
                        .Query(player.playerId)
                    )
                )
            );

            await SendPlayerInfoAsync(player, offenseResponse.Documents);
        }
        catch (Exception ex)
        {
            // Implement logging
            Console.WriteLine(ex.Message + ex.StackTrace);
            await ReplyAsync("Gross... I just swallowed a bug. GET IT OUT OF ME!");
            await ReplyAsync(ex.Message);
        }
    }

    private static EmbedBuilder BuildPlayerEmbed(PlayerRecord player, IEnumerable<OffenseReport> offenses)
    {
        // Add player details to the embed
        var embed = new EmbedBuilder
        {
            Title = $"Player Information: {player.playerAlliance} {player.playerName} ({player.playerId})",
            Color = Color.Blue
        };

        var redFlag = new StringBuilder();
        redFlag.AppendLine($"{player.redFlag} - {player.redFlagReason}");
        embed.AddField("Red Flag Status:", player.redFlag);
        if (player.redFlag)
        {
            embed.AddField("Reason:", player.redFlagReason);
        }

        embed.AddField("Known Names:", string.Join(", ", player.knownNames));
        embed.AddField("Known alliances:", string.Join(", ", player.knownAlliances));

        // Add offenses
        var offenseReports = offenses.ToList();
        if (offenseReports.Any())
        {
            var incidents = new StringBuilder();
            var count = 1;
            foreach (var offense in offenseReports)
            {
                incidents.AppendLine($"{count++}: {offense.date.ToString("MM/dd/yyyy")} - {offense.offenseType} - {offense.reportId}");
            }
            embed.AddField("Incidents:", incidents.ToString(), false);
        }
        else
        {
            embed.AddField("Incidents:", "No player offenses recorded", false);
        }

        return embed;
    }
    public async Task SendPlayerInfoAsync(PlayerRecord player, IEnumerable<OffenseReport> offenses)
    {
        var offenseReports = offenses.ToList();
        var embed = BuildPlayerEmbed(player, offenseReports).Build();
        var components = CreateReportButtons(offenseReports).Build();

        await ReplyAsync(embed: embed, components: components);
    }

    // Method to create buttons for each offense report
    private ComponentBuilder CreateReportButtons(IEnumerable<OffenseReport> offenses)
    {
        var componentBuilder = new ComponentBuilder();
        var count = 1;
        foreach (var offense in offenses)
        {
            componentBuilder.WithButton($"Get Report {count++}", $"report_{offense.reportId}", ButtonStyle.Primary);
        }
        return componentBuilder;
    }


    [Command("offense")]
    public async Task GetOffenseReportEmbedCommand(string reportId)
    {
        try
        {
            // Fetch offense report
            var offenseReport = await ElasticClient.GetOffenseReportByIdAsync(reportId);

            // Download evidence files and prepare attachments
            var attachments = new List<FileAttachment>();
            foreach (var url in offenseReport.evidenceUrls)
            {
                var file = await _s3Service.DownloadFileAsync(url);
                if (file != null)
                {
                    attachments.Add((FileAttachment)file);
                }
            }

            // Send the message with embed and attachments
            await Context.Channel.SendFilesAsync(attachments, embed: offenseReport.Embed().Build());
        }
        catch (Exception ex)
        {
            // Implement logging
            Console.WriteLine(ex.Message + ex.StackTrace);
            await ReplyAsync("Gross... I just swallowed a bug. GET IT OUT OF ME!");
            await ReplyAsync(ex.Message);
        }
    }

    
}
