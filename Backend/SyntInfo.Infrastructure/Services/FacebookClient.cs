using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.Infrastructure.Services;

public class FacebookClient : IFacebookClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FacebookClient> _logger;

    public FacebookClient(HttpClient httpClient, IConfiguration configuration, ILogger<FacebookClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        var baseUrl = _configuration["SocialPlatforms:Facebook:BaseUrl"] ?? "https://graph.facebook.com/v19.0/";
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<string> PublishPagePostAsync(string message, string? link = null, CancellationToken cancellationToken = default)
    {
        var pageId = _configuration["SocialPlatforms:Facebook:PageId"];
        var pageAccessToken = _configuration["SocialPlatforms:Facebook:PageAccessToken"];
        var isMock = _configuration.GetValue("SocialPlatforms:Facebook:UseMock", false);

        if (isMock || string.IsNullOrWhiteSpace(pageAccessToken) || pageAccessToken.Contains("PLACEHOLDER") || string.IsNullOrWhiteSpace(pageId))
        {
            _logger.LogInformation("FacebookClient [MOCK/TEST]: Publikowanie posta na Facebooku (PageId: {PageId}, Długość: {Length})",
                pageId ?? "mock-page", message.Length);
            return $"fb-mock-post-{Guid.NewGuid():N}[..8]";
        }

        var postData = new Dictionary<string, string>
        {
            { "message", message },
            { "access_token", pageAccessToken }
        };

        if (!string.IsNullOrWhiteSpace(link))
        {
            postData.Add("link", link);
        }

        using var content = new FormUrlEncodedContent(postData);
        var response = await _httpClient.PostAsync($"{pageId}/feed", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd podczas publikowania posta na Facebooku (HTTP {StatusCode}): {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Facebook Graph API zwróciło status {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("id", out var idEl))
        {
            return idEl.GetString() ?? "fb-post-id";
        }

        return "facebook-post-published";
    }
}
