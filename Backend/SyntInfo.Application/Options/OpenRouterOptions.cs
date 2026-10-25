namespace SyntInfo.Application.Options;

public class OpenRouterOptions : IConfigurableOptions
{
    public const string SectionName = "OpenRouter";
    string IConfigurableOptions.SectionName => SectionName;

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1/";
    public string AnalystModel { get; set; } = "google/gemma-4-31b-it:free";
    public string EditorModel { get; set; } = "meta-llama/llama-3.3-70b-instruct";
    public string FallbackModel { get; set; } = "openrouter/free";
    public string EmbeddingModel { get; set; } = "openai/text-embedding-3-small";
}
