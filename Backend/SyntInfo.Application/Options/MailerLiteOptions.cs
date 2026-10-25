namespace SyntInfo.Application.Options;

public class MailerLiteOptions : IConfigurableOptions, IHttpClientOptions
{
    public const string SectionName = "SocialPlatforms:MailerLite";
    string IConfigurableOptions.SectionName => SectionName;

    public const string HttpClientName = "MailerLiteClient";
    string IHttpClientOptions.HttpClientName => HttpClientName;

    public string BaseUrl { get; set; } = "https://connect.mailerlite.com/api/";
    public string? ApiKey { get; set; }
    public string FromEmail { get; set; } = "kontakt@infoskrot.pl";
    public string FromName { get; set; } = "InfoSkrót";
    public bool Enabled { get; set; } = true;
    public bool UseMock { get; set; }
}
