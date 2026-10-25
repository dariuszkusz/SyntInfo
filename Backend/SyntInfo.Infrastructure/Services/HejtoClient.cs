using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace SyntInfo.Infrastructure.Services;

public class HejtoClient : IHejtoClient
{
    private const string TokenCacheKey = "hejto_oauth_access_token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly HejtoOptions _hejtoOptions;
    private readonly ILogger<HejtoClient> _logger;

    public HejtoClient(
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        IOptions<HejtoOptions> hejtoOptions,
        ILogger<HejtoClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _hejtoOptions = hejtoOptions.Value;
        _logger = logger;
    }

    public async Task<string> CreatePostAsync(string content, string? communitySlug = null, CancellationToken cancellationToken = default)
    {
        var targetCommunity = communitySlug ?? _hejtoOptions.DefaultCommunity;
        var isMock = _hejtoOptions.UseMock;

        string? accessToken = null;
        try
        {
            accessToken = await GetOrRefreshTokenAsync(cancellationToken);
        }
        catch (Exception ex) when (isMock)
        {
            _logger.LogWarning(ex, "HejtoClient: Błąd autoryzacji OAuth2, lecz włączony jest tryb UseMock.");
        }

        if (isMock || string.IsNullOrWhiteSpace(accessToken) || accessToken.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("HejtoClient [MOCK/TEST]: Publikowanie wpisu na Hejto (Społeczność: {Community}, Długość: {Length})",
                targetCommunity, content.Length);
            return $"hejto-mock-{Guid.NewGuid():N}[..8]";
        }

        // Hejto wymaga co najmniej jednego tagu (#tag) w treści wpisu
        var finalContent = content;
        if (!finalContent.Contains('#'))
        {
            finalContent = $"{finalContent.Trim()}\n\n#wiadomosci #infoskrot #polityka";
        }

        var baseUrl = _hejtoOptions.BaseUrl;
        if (!baseUrl.EndsWith("/")) baseUrl += "/";

        var client = _httpClientFactory.CreateClient("HejtoClient");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "posts"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            content = finalContent,
            community = targetCommunity,
            type = "discussion"
        });

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd podczas publikowania wpisu na Hejto (HTTP {StatusCode}): {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Hejto API zwróciło status {response.StatusCode}: {errorBody}");
        }

        // Hejto przy sukcesie zwraca HTTP 201 Created z nagłówkiem Location
        if (response.Headers.Location != null)
        {
            var segments = response.Headers.Location.OriginalString.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                return segments[^1];
            }
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(responseJson))
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("slug", out var slugEl))
            {
                return slugEl.GetString() ?? "hejto-post";
            }

            if (doc.RootElement.TryGetProperty("uuid", out var uuidEl))
            {
                return uuidEl.GetString() ?? "hejto-post";
            }
        }

        return "hejto-post-published";
    }

    private async Task<string?> GetOrRefreshTokenAsync(CancellationToken cancellationToken)
    {
        // 1. Sprawdź IMemoryCache
        if (_memoryCache.TryGetValue(TokenCacheKey, out string? cachedToken) && !string.IsNullOrWhiteSpace(cachedToken))
        {
            return cachedToken;
        }

        // 2. Sprawdź czy zdefiniowano bezpośredni token statyczny
        var directToken = _hejtoOptions.Token;
        var clientId = _hejtoOptions.ClientId;
        var clientSecret = _hejtoOptions.ClientSecret;

        if (string.IsNullOrWhiteSpace(clientId) || clientId.Contains("PLACEHOLDER") ||
            string.IsNullOrWhiteSpace(clientSecret) || clientSecret.Contains("PLACEHOLDER"))
        {
            if (!string.IsNullOrWhiteSpace(directToken) && !directToken.Contains("PLACEHOLDER"))
            {
                return directToken;
            }

            return null;
        }

        // 3. Pobierz nowy token z serwera autoryzacji Hejto
        var authUrl = _hejtoOptions.AuthUrl;
        _logger.LogInformation("Pobieranie nowego tokena OAuth2 z Hejto ({AuthUrl})...", authUrl);

        var authClient = _httpClientFactory.CreateClient();
        using var authRequest = new HttpRequestMessage(HttpMethod.Post, authUrl);
        authRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        authRequest.Content = JsonContent.Create(new
        {
            client_id = clientId.Trim(),
            client_secret = clientSecret.Trim(),
            grant_type = "client_credentials"
        });

        using var authResponse = await authClient.SendAsync(authRequest, cancellationToken);

        if (!authResponse.IsSuccessStatusCode)
        {
            var errorBody = await authResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd autoryzacji Hejto OAuth2 (HTTP {StatusCode}): {Error}", authResponse.StatusCode, errorBody);
            throw new HttpRequestException($"Błąd autoryzacji Hejto OAuth2: {errorBody}");
        }

        var authJson = await authResponse.Content.ReadAsStringAsync(cancellationToken);
        using var authDoc = JsonDocument.Parse(authJson);

        if (authDoc.RootElement.TryGetProperty("access_token", out var tokenEl))
        {
            var token = tokenEl.GetString();
            var expiresInSeconds = authDoc.RootElement.TryGetProperty("expires_in", out var expEl) && expEl.TryGetInt32(out var exp)
                ? exp
                : 86400;

            // Zapis do IMemoryCache z 60-sekundowym buforem bezpieczeństwa
            var cacheDuration = TimeSpan.FromSeconds(Math.Max(60, expiresInSeconds - 60));
            _memoryCache.Set(TokenCacheKey, token, cacheDuration);

            _logger.LogInformation("Pomyślnie uzyskano i zachowano w pamięci cache access_token z Hejto OAuth2 (Ważny przez {Seconds}s).", expiresInSeconds);
            return token;
        }

        throw new InvalidOperationException("Odpowiedź z serwera autoryzacji Hejto nie zawiera pola access_token.");
    }
}
