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

[Export(typeof(ILanguageService))]
public class AWSLanguageService : LoggingResource, ILanguageService
{
    protected AWSLanguageService() : base(nameof(AWSLanguageService)) { }

    public async Task<string> DetectLanguageAsync(string text)
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
