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
        {"🇵🇭", "tl" },
        // Additional languages without a country flag
        {"🇪🇺", "nl"}, // Dutch for European Union flag
        // ... Add any additional mappings as necessary
    };


    [DiscordEventHandler("ReactionAdded")]
    public async Task OnFlagEmojiAdded(Cacheable<IUserMessage, ulong> cacheableMessage, Cacheable<IMessageChannel, ulong> cacheableChannel, SocketReaction reaction)
    {
        // Check if the reaction is a flag emoji and get the corresponding language code
        if (_emojiLanguageMap.TryGetValue(reaction.Emote.Name, out var languageCode))
        {
            var message = await cacheableMessage.GetOrDownloadAsync();
            var channel = await cacheableChannel.GetOrDownloadAsync();
            var user = reaction.User.IsSpecified ? reaction.User.Value : await channel.GetUserAsync(reaction.UserId) as SocketGuildUser;
            var textToTranslate = message.Content;
            string nickName;
            // Ensure the channel is within a guild
            if (channel is SocketGuildChannel socketGuildChannel)
            {
                var socketGuildUser = socketGuildChannel.Guild.GetUser(reaction.UserId);
                nickName = socketGuildUser?.Nickname ?? socketGuildUser?.GlobalName ?? user?.Username;
            }
            else
            {
                Logger.LogWarning("Channel is not within a guild, or the guild is not cached.");
                nickName = user?.GlobalName ?? user?.Username;
            }
            try
            {
                // Perform translation
                var translateRequest = new TranslateTextRequest
                {
                    Text = textToTranslate,
                    TargetLanguageCode = languageCode,
                    SourceLanguageCode = "auto"
                };
                using var client = new AmazonTranslateClient();
                var result = await client.TranslateTextAsync(translateRequest);

                // Create the embed (card) with the translated text
                if (result.HttpStatusCode == HttpStatusCode.OK)
                {
                    var embedBuilder = new EmbedBuilder()
                        .WithAuthor(nickName, user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                        .WithDescription(result.TranslatedText)
                        .WithColor(new Color(0, 255, 0)); // You can change the color

                    await channel.SendMessageAsync(embed: embedBuilder.Build());
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
