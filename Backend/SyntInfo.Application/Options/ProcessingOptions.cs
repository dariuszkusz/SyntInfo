namespace SyntInfo.Application.Options;

public class ProcessingOptions
{
    public const string SectionName = "ProcessingSettings";

    public int MaxArticlesPerRegion { get; set; } = 10;
    public int MaxPositiveArticles { get; set; } = 3;
}
