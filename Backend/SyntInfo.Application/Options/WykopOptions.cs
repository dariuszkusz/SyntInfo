namespace SyntInfo.Application.Options;

public class WykopOptions : IConfigurableOptions, IHttpClientOptions
{
    public const string SectionName = "SocialPlatforms:Wykop";
    string IConfigurableOptions.SectionName => SectionName;

    public const string HttpClientName = "WykopClient";
    string IHttpClientOptions.HttpClientName => HttpClientName;

    public string BaseUrl { get; set; } = "https://wykop.pl/api/v3/";
    public string? Key { get; set; }
    public string? Secret { get; set; }
    public string? Token { get; set; }
    public string? UserToken { get; set; }
    public string? RefreshToken { get; set; }
    public string AuthUrl { get; set; } = "https://wykop.pl/api/v3/auth";
    public bool UseMock { get; set; }
}
