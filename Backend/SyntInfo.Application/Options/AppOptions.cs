namespace SyntInfo.Application.Options;

public class AppOptions : IConfigurableOptions
{
    public const string SectionName = "";
    string IConfigurableOptions.SectionName => SectionName;

    public string FrontendUrl { get; set; } = "https://infoskrot.pl";
}

