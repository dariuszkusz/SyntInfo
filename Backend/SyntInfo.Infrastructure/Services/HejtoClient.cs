using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.Infrastructure.Services;

public class HejtoClient : IHejtoClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HejtoClient> _logger;

    public HejtoClient(HttpClient httpClient, IConfiguration configuration, ILogger<HejtoClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        var baseUrl = _configuration["SocialPlatforms:Hejto:BaseUrl"] ?? "https://api.hejto.pl/";
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<string> CreatePostAsync(string content, string? communitySlug = null, CancellationToken cancellationToken = default)
    {
        var token = _configuration["SocialPlatforms:Hejto:Token"];
        var isMock = _configuration.GetValue("SocialPlatforms:Hejto:UseMock", false);
        var targetCommunity = communitySlug ?? _configuration["SocialPlatforms:Hejto:DefaultCommunity"] ?? "wiadomosci";

        if (isMock || string.IsNullOrWhiteSpace(token) || token.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("HejtoClient [MOCK/TEST]: Publikowanie wpisu na Hejto (Społeczność: {Community}, Długość: {Length})",
                targetCommunity, content.Length);
            return $"hejto-mock-{Guid.NewGuid():N}[..8]";
        }

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            content,
            community = targetCommunity,
            type = "discussion"
        };

        var response = await _httpClient.PostAsJsonAsync("posts", payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd podczas publikowania wpisu na Hejto (HTTP {StatusCode}): {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Hejto API zwróciło status {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("slug", out var slugEl))
        {
            return slugEl.GetString() ?? "hejto-post";
        }

        if (doc.RootElement.TryGetProperty("uuid", out var uuidEl))
        {
            return uuidEl.GetString() ?? "hejto-post";
        }

        return "hejto-post-published";
    }
}
