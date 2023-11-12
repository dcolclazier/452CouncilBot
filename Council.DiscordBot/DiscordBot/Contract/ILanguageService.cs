using System.Threading.Tasks;

public interface ILanguageService
{
    Task<string> DetectLanguageAsync(string text);
    Task<string> TranslateTextAsync(string sourceText, string sourceLanguage, string targetLanguage);
}
