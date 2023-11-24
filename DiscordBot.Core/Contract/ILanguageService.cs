using System.Collections.Generic;
using System.Threading.Tasks;
namespace DiscordBot.Core.Contract
{
    public interface ILanguageService
    {
        Task<string?> DetectLanguageAsync(string text);
        Task<string> TranslateTextAsync(string sourceText, string sourceLanguage, string targetLanguage);
    }
}