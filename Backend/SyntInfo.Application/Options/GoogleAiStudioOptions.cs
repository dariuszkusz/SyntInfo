namespace SyntInfo.Application.Options;

public class GoogleAiStudioOptions : IConfigurableOptions
{
    public const string SectionName = "GoogleAiStudio";
    string IConfigurableOptions.SectionName => SectionName;

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-3.5-flash";
    public string FallbackModel { get; set; } = "gemini-3.1-flash-lite";
}

