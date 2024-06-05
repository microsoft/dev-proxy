public interface ILanguageModelClient
{
    Task<string?> GenerateCompletion(string prompt);
}