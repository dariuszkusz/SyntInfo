using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.Infrastructure.Services;

public class WykopClient : IWykopClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WykopClient> _logger;

    public WykopClient(HttpClient httpClient, IConfiguration configuration, ILogger<WykopClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        var baseUrl = _configuration["SocialPlatforms:Wykop:BaseUrl"] ?? "https://wykop.pl/api/v3/";
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<string> CreateEntryAsync(string content, string? photoKey = null, CancellationToken cancellationToken = default)
    {
        var token = _configuration["SocialPlatforms:Wykop:Token"];
        var isMock = _configuration.GetValue("SocialPlatforms:Wykop:UseMock", false);

        if (isMock || string.IsNullOrWhiteSpace(token) || token.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("WykopClient [MOCK/TEST]: Publikowanie wpisu na Wykopie (Długość: {Length})", content.Length);
            return $"wykop-mock-{Guid.NewGuid():N}[..8]";
        }

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            data = new
            {
                content,
                photo = photoKey
            }
        };

        var response = await _httpClient.PostAsJsonAsync("entries", payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd podczas publikowania wpisu na Wykop v3 (HTTP {StatusCode}): {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Wykop API zwróciło status {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("id", out var idEl))
        {
            return idEl.ToString();
        }

        return "wykop-entry-published";
    }
}
