namespace SyntInfo.Application.Options;

public class TavilyOptions : IConfigurableOptions
{
    public const string SectionName = "Search";
    string IConfigurableOptions.SectionName => SectionName;

    public string TavilyApiKey { get; set; } = string.Empty;
}
