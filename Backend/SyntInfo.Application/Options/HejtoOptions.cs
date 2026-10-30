namespace SyntInfo.Application.Options;

public class HejtoOptions : IConfigurableOptions, IHttpClientOptions
{
    public const string SectionName = "SocialPlatforms:Hejto";
    string IConfigurableOptions.SectionName => SectionName;

    public const string HttpClientName = "HejtoClient";
    string IHttpClientOptions.HttpClientName => HttpClientName;

    public string BaseUrl { get; set; } = "https://api.hejto.pl/";
    public string AuthUrl { get; set; } = "https://auth.hejto.pl/token";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string DefaultCommunity { get; set; } = "wiadomosci-polska";
    public string? Token { get; set; }
    public bool UseMock { get; set; }
    public bool PublishArticlesSeparately { get; set; } = true;
    public int DelayBetweenPostsSeconds { get; set; } = 60;
}
