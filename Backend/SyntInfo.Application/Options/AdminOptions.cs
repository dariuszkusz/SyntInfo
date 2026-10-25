namespace SyntInfo.Application.Options;

public class AdminOptions : IConfigurableOptions
{
    public const string SectionName = "Admin";
    string IConfigurableOptions.SectionName => SectionName;

    public string ApiKey { get; set; } = string.Empty;
}

