namespace SyntInfo.Application.Options;

public class CurationOptions : IConfigurableOptions
{
    public const string SectionName = "SocialPlatforms";
    string IConfigurableOptions.SectionName => SectionName;

    public int MaxArticlesPerRun { get; set; }
    public int MaxPolandArticles { get; set; }
    public int MaxWorldArticles { get; set; }
    public int MaxPositiveArticles { get; set; }

    public int ProposalPolandCount { get; set; }
    public int ProposalWorldCount { get; set; }
    public int ProposalPositiveCount { get; set; }

    public int GetMaxSelectionLimit(SourceRegion region) => region switch
    {
        SourceRegion.Poland => MaxPolandArticles,
        SourceRegion.World => MaxWorldArticles,
        SourceRegion.Positive => MaxPositiveArticles,
        _ => MaxArticlesPerRun
    };

    public int GetProposalLimit(SourceRegion region) => region switch
    {
        SourceRegion.Poland => ProposalPolandCount,
        SourceRegion.World => ProposalWorldCount,
        SourceRegion.Positive => ProposalPositiveCount,
        _ => ProposalPolandCount
    };
}
