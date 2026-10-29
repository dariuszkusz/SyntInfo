namespace SyntInfo.Application.Options;

public class ProcessingOptions : IConfigurableOptions
{
    public const string SectionName = "ProcessingSettings";
    string IConfigurableOptions.SectionName => SectionName;

    public int MaxArticlesPerRegion { get; set; }
    public int MaxPositiveArticles { get; set; }
}
