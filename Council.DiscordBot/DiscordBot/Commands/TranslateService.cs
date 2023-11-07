using Discord.Commands;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System;
using System.Net;
using Amazon.Translate;
using Amazon.Translate.Model;
using System.Composition;
using Council.DiscordBot.Core;
using AWS.Logging.Contract;
using Discord.WebSocket;
using Discord;

namespace DiscordBot.Commands.Commands;

public class TranslateService : ModuleBase<SocketCommandContext>
{
    [Import]
    private IServiceLoggerFactory LogFactory { get; set; }
    private ILogger Logger { get; }

    // Use a flag emoji as a key to represent the language in the map
    private readonly Dictionary<string, string> _emojiLanguageMap = new Dictionary<string, string>
    {
        {"🇦🇪", "ar"}, // Arabic
        {"🇧🇩", "bn"}, // Bengali
        {"🇨🇿", "cs"}, // Czech
        {"🇩🇰", "da"}, // Danish
        {"🇩🇪", "de"}, // German
        {"🇬🇷", "el"}, // Greek
        {"🇦🇺", "en"}, // English
        {"🇬🇧", "en"}, // English
        {"🇺🇸", "en"}, // English
        {"🇪🇸", "es"}, // Spanish
        {"🇲🇽", "es"}, // Spanish
        {"🇦🇷", "es"}, // Spanish
        {"🇪🇪", "et"}, // Estonian
        {"🇫🇮", "fi"}, // Finnish
        {"🇫🇷", "fr"}, // French
        {"🇮🇱", "he"}, // Hebrew
        {"🇮🇳", "hi"}, // Hindi
        {"🇭🇺", "hu"}, // Hungarian
        {"🇮🇩", "id"}, // Indonesian
        {"🇮🇹", "it"}, // Italian
        {"🇯🇵", "ja"}, // Japanese
        {"🇰🇷", "ko"}, // Korean
        {"🇲🇸", "ms"}, // Malay
        {"🇳🇴", "no"}, // Norwegian
        {"🇮🇷", "fa"}, // Persian
        {"🇵🇱", "pl"}, // Polish
        {"🇧🇷", "pt"}, // Portuguese
        {"🇵🇹", "pt"}, // Portuguese
        {"🇷🇴", "ro"}, // Romanian
        {"🇷🇺", "ru"}, // Russian
        {"🇸🇦", "ar"}, // Arabic
        {"🇸🇪", "sv"}, // Swedish
        {"🇹🇭", "th"}, // Thai
        {"🇹🇷", "tr"}, // Turkish
        {"🇵🇰", "ur"}, // Urdu
        {"🇻🇳", "vi"}, // Vietnamese
        {"🇨🇳", "zh"}, // Chinese Simplified
        {"🇹🇼", "zh-TW"}, // Chinese Traditional
        // Additional languages without a country flag
        {"🇪🇺", "nl"}, // Dutch for European Union flag
        // ... Add any additional mappings as necessary
    };



    [DiscordEventHandler("ReactionAdded")]
    public async Task OnReactionAddedAsync(Cacheable<IUserMessage, ulong> cacheableMessage, ISocketMessageChannel channel, SocketReaction reaction)
    {
        // Check if the reaction is a flag emoji and get the corresponding language code
        if (_emojiLanguageMap.TryGetValue(reaction.Emote.Name, out var languageCode))
        {
            var message = await cacheableMessage.GetOrDownloadAsync();
            var textToTranslate = message.Content;
            using var client = new AmazonTranslateClient();
            try
            {
                // Perform translation
                var translateRequest = new TranslateTextRequest
                {
                    Text = textToTranslate,
                    TargetLanguageCode = languageCode,
                    SourceLanguageCode = "auto"
                };
                var result = await client.TranslateTextAsync(translateRequest);

                // Send the translated message
                if (result.HttpStatusCode == HttpStatusCode.OK)
                {
                    await channel.SendMessageAsync(result.TranslatedText);
                }
                else
                {
                    Logger.LogError("Translation failed with status code: {StatusCode}", result.HttpStatusCode);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception occurred during translation");
            }
        }
    }

    public TranslateService()
    {
        Logger = LogFactory?.GetLogger(nameof(TranslateService));
    }
}
