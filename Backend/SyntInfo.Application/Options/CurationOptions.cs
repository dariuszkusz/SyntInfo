namespace SyntInfo.Application.Options;

public class CurationOptions : IConfigurableOptions
{
    public const string SectionName = "SocialPlatforms";
    string IConfigurableOptions.SectionName => SectionName;

    public int MaxArticlesPerRun { get; set; } = 5;
}

