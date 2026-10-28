namespace SyntInfo.Application.Options;

public class CurationOptions : IConfigurableOptions
{
    public const string SectionName = "SocialPlatforms";
    string IConfigurableOptions.SectionName => SectionName;

    public int MaxArticlesPerRun { get; set; } = 5;
    public int MaxPolandArticles { get; set; } = 5;
    public int MaxWorldArticles { get; set; } = 5;
    public int MaxPositiveArticles { get; set; } = 3;

    public int ProposalPolandCount { get; set; } = 10;
    public int ProposalWorldCount { get; set; } = 10;
    public int ProposalPositiveCount { get; set; } = 6;
}

