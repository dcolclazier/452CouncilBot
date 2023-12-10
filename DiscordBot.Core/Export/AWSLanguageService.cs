using System;
using System.Threading.Tasks;
using System.Linq;
using Amazon.Translate.Model;
using Amazon.Translate;
using Amazon.Comprehend;
using Amazon.Comprehend.Model;
using System.Composition;
using AWS.Logging;
using Microsoft.Extensions.Logging;
using DiscordBot.Core.Contract;
using Amazon.S3.Model;
using Nest;
using Amazon.Runtime;
using Elasticsearch.Net.Aws;
using Elasticsearch.Net;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;
using Amazon;
using Newtonsoft.Json;

[Export(typeof(IElasticsearchService))]

[Shared]
public class Elasticsearch710Service : LoggingResource, IElasticsearchService
{
    private readonly string _esEndpoint = Environment.GetEnvironmentVariable("ES_ENDPOINT");
    private readonly ElasticClient _elasticClient;

    public Elasticsearch710Service() : base(nameof(Elasticsearch710Service)) 
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
                .DisableDirectStreaming()
                .OnRequestCompleted(callDetails =>
                {
                    if (callDetails.RequestBodyInBytes == null) return;
                    
                    var requestBody = Encoding.UTF8.GetString(callDetails.RequestBodyInBytes);
                    var prettyJsonRequest = JToken.Parse(requestBody).ToString(Formatting.Indented);
                    Logger.LogInformation(prettyJsonRequest);
                });
            _elasticClient = new ElasticClient(settings);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex.Message);
            Logger.LogError(ex.StackTrace);
        }

    }

    public Task<string> CreateOffenseReportAsync(OffenseReport report)
    {
        throw new NotImplementedException();
    }

    public Task<string> CreateOrUpdatePlayerAsync(PlayerRecord player)
    {
        throw new NotImplementedException();
    }

    public async Task<OffenseReport> GetOffenseReportByIdAsync(string reportId)
    {
        var response = await _elasticClient.SearchAsync<OffenseReport>(s => s
            .Index("offense_reports")
            .Query(q => q
                .Term(t => t
                    .Field(f => f.reportId)
                    .Value(reportId)
                )
            )
        );

        return response.Documents.First();
    }

    public Task<PlayerRecord> GetPlayerByIdAsync(string playerId)
    {
        throw new NotImplementedException();
    }

    public Task<ElasticClient> InitializeElasticsearchClientAsync(string defaultIndex)
    {
        throw new NotImplementedException();
    }
}




[Export(typeof(ILanguageService))]
public class AWSLanguageService : LoggingResource, ILanguageService
{
    public AWSLanguageService() : base(nameof(AWSLanguageService)) { }

    public async Task<string?> DetectLanguageAsync(string text)
    {
        try
        {
            using var cliient = new AmazonComprehendClient();
            var detectLanguageRequest = new DetectDominantLanguageRequest
            {
                Text = text
            };
            var detectLanguageResponse = await cliient.DetectDominantLanguageAsync(detectLanguageRequest);
            return detectLanguageResponse.Languages.OrderByDescending(l => l.Score).FirstOrDefault()?.LanguageCode;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error detecting language: {ex.Message} {ex.StackTrace}");
            return "en";
        }
    }

    public async Task<string> TranslateTextAsync(string text, string sourceLanguageCode, string targetLanguageCode)
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
            var translateResponse = await client.TranslateTextAsync(translateRequest);
            return translateResponse.TranslatedText;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error translating from {sourceLanguageCode} to {targetLanguageCode}: {ex.Message} {ex.StackTrace}");
            return text;
        }
    }
}
