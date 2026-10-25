namespace SyntInfo.Application.Options;

public class FacebookOptions : IConfigurableOptions, IHttpClientOptions
{
    public const string SectionName = "SocialPlatforms:Facebook";
    string IConfigurableOptions.SectionName => SectionName;

    public const string HttpClientName = "FacebookClient";
    string IHttpClientOptions.HttpClientName => HttpClientName;

    public string BaseUrl { get; set; } = "https://graph.facebook.com/v19.0/";
    public string? PageId { get; set; }
    public string? PageAccessToken { get; set; }
    public bool UseMock { get; set; } = true;
}

