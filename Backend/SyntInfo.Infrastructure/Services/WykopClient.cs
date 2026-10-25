using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace SyntInfo.Infrastructure.Services;

public class WykopClient : IWykopClient
{
    private const string AppTokenCacheKey = "wykop_app_oauth_token";
    private const string UserTokenCacheKey = "wykop_user_oauth_token";
    private const string RefreshTokenCacheKey = "wykop_user_refresh_token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly WykopOptions _wykopOptions;
    private readonly ILogger<WykopClient> _logger;

    public WykopClient(
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        IOptions<WykopOptions> wykopOptions,
        ILogger<WykopClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _wykopOptions = wykopOptions.Value;
        _logger = logger;
    }

    public async Task<string> GetConnectUrlAsync(CancellationToken cancellationToken = default)
    {
        var appToken = await GetOrRefreshAppTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(appToken))
        {
            throw new InvalidOperationException("Nie udało się pobrać tokenu aplikacji Wykop. Upewnij się, że SocialPlatforms:Wykop:Key oraz SocialPlatforms:Wykop:Secret są skonfigurowane.");
        }

        var baseUrl = GetBaseUrl();
        var client = _httpClientFactory.CreateClient("WykopClient");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(baseUrl), "connect"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Błąd podczas pobierania connect_url z Wykop API v3 (HTTP {StatusCode}): {Response}", response.StatusCode, responseContent);
            throw new HttpRequestException($"Wykop GET /connect zwrócił status {response.StatusCode}: {responseContent}");
        }

        using var doc = JsonDocument.Parse(responseContent);
        if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("connect_url", out var urlEl))
        {
            return urlEl.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("Odpowiedź z endpointu /connect nie zawierała pola data.connect_url.");
    }

    private static string TokenFilePath
    {
        get
        {
            var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
            if (Directory.Exists(logsDir))
            {
                return Path.Combine(logsDir, "wykop_tokens.json");
            }
            return Path.Combine(AppContext.BaseDirectory, "wykop_tokens.json");
        }
    }

    public async Task SaveUserTokensAsync(string userToken, string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
        {
            throw new ArgumentException("UserToken nie może być pusty.", nameof(userToken));
        }

        // Token użytkownika zazwyczaj ważny jest 24h, zachowajmy w cache
        _memoryCache.Set(UserTokenCacheKey, userToken.Trim(), TimeSpan.FromHours(23));

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            _memoryCache.Set(RefreshTokenCacheKey, refreshToken.Trim(), TimeSpan.FromDays(30));
        }

        try
        {
            var data = new
            {
                UserToken = userToken.Trim(),
                RefreshToken = refreshToken?.Trim(),
                SavedAt = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(TokenFilePath, json, cancellationToken);
            _logger.LogInformation("Zapisano trwały token użytkownika Wykop w pliku {Path}", TokenFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się zapisać tokenów Wykop do pliku {Path}", TokenFilePath);
        }

        _logger.LogInformation("Zapisano token użytkownika Wykop w pamięci podręcznej.");
    }


    public async Task<string?> UploadPhotoAsync(string imageUrlOrPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrlOrPath)) return null;

        var userToken = await GetOrRefreshUserTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(userToken))
        {
            _logger.LogWarning("Brak tokena użytkownika Wykop do wysłania zdjęcia.");
            return null;
        }

        try
        {
            byte[] imageBytes;
            string fileName = "photo.jpg";

            if (imageUrlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                imageUrlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var downloadClient = _httpClientFactory.CreateClient();
                imageBytes = await downloadClient.GetByteArrayAsync(imageUrlOrPath, cancellationToken);
                var uri = new Uri(imageUrlOrPath);
                var lastSegment = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(lastSegment) && lastSegment.Contains('.'))
                {
                    fileName = lastSegment;
                }
            }
            else if (File.Exists(imageUrlOrPath))
            {
                imageBytes = await File.ReadAllBytesAsync(imageUrlOrPath, cancellationToken);
                fileName = Path.GetFileName(imageUrlOrPath);
            }
            else
            {
                _logger.LogWarning("Nieprawidłowa ścieżka lub URL zdjęcia: {Path}", imageUrlOrPath);
                return null;
            }

            var baseUrl = GetBaseUrl();
            var wykopClient = _httpClientFactory.CreateClient("WykopClient");
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "media/photos/upload?type=entries"));
            uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            uploadRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var form = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(imageBytes);
            byteContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(byteContent, "file", fileName);
            uploadRequest.Content = form;

            using var uploadResponse = await wykopClient.SendAsync(uploadRequest, cancellationToken);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var err = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Nie udało się wysłać zdjęcia do Wykop v3 (HTTP {StatusCode}): {Error}", uploadResponse.StatusCode, err);
                return null;
            }

            var json = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("key", out var keyEl))
            {
                var key = keyEl.GetString();
                _logger.LogInformation("Pomyślnie wysłano zdjęcie do Wykopu. Klucz zdjęcia: {Key}", key);
                return key;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wystąpił błąd podczas przesyłania zdjęcia do Wykopu: {Url}", imageUrlOrPath);
        }

        return null;
    }

    public async Task<string> CreateEntryAsync(string content, string? photoKey = null, CancellationToken cancellationToken = default)

    {
        var isMock = _wykopOptions.UseMock;

        string? userToken = null;
        try
        {
            userToken = await GetOrRefreshUserTokenAsync(cancellationToken);
        }
        catch (Exception ex) when (isMock)
        {
            _logger.LogWarning(ex, "WykopClient: Błąd autoryzacji Wykop, lecz włączony jest tryb UseMock.");
        }

        if (isMock || string.IsNullOrWhiteSpace(userToken) || userToken.Contains("PLACEHOLDER"))
        {
            if (!isMock && string.IsNullOrWhiteSpace(userToken))
            {
                var connectUrl = string.Empty;
                try
                {
                    connectUrl = await GetConnectUrlAsync(cancellationToken);
                }
                catch { }

                throw new InvalidOperationException(
                    "Brak autoryzacji użytkownika Wykop (ROLE_USER). " +
                    (!string.IsNullOrEmpty(connectUrl) ? $"Otwórz w przeglądarce link autoryzacyjny: {connectUrl}" : "Wywołaj /api/Wykop/connect-url, aby autoryzować konto."));
            }

            _logger.LogInformation("WykopClient [MOCK/TEST]: Publikowanie wpisu na Wykopie (Długość: {Length})", content.Length);
            return $"wykop-mock-{Guid.NewGuid():N}"[..8];
        }

        var baseUrl = GetBaseUrl();
        var client = _httpClientFactory.CreateClient("WykopClient");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "entries"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            data = new
            {
                content,
                photo = photoKey
            }
        });

        using var response = await client.SendAsync(request, cancellationToken);

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

    private async Task<string?> GetOrRefreshUserTokenAsync(CancellationToken cancellationToken)
    {
        // 1. Sprawdź pamięć podręczną
        if (_memoryCache.TryGetValue(UserTokenCacheKey, out string? cachedUserToken) && !string.IsNullOrWhiteSpace(cachedUserToken))
        {
            return cachedUserToken;
        }

        // 1b. Sprawdź trwały plik wykop_tokens.json
        var tokenPath = File.Exists(TokenFilePath)
            ? TokenFilePath
            : Path.Combine(AppContext.BaseDirectory, "wykop_tokens.json");

        if (File.Exists(tokenPath))
        {
            try
            {
                var fileContent = await File.ReadAllTextAsync(TokenFilePath, cancellationToken);
                using var doc = JsonDocument.Parse(fileContent);
                if (doc.RootElement.TryGetProperty("UserToken", out var uTokenEl))
                {
                    var fileUserToken = uTokenEl.GetString();
                    if (!string.IsNullOrWhiteSpace(fileUserToken))
                    {
                        _memoryCache.Set(UserTokenCacheKey, fileUserToken.Trim(), TimeSpan.FromHours(12));
                        if (doc.RootElement.TryGetProperty("RefreshToken", out var rTokenEl))
                        {
                            var fileRefresh = rTokenEl.GetString();
                            if (!string.IsNullOrWhiteSpace(fileRefresh))
                            {
                                _memoryCache.Set(RefreshTokenCacheKey, fileRefresh.Trim(), TimeSpan.FromDays(30));
                            }
                        }
                        return fileUserToken.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Błąd odczytu tokenów Wykop z pliku {Path}", TokenFilePath);
            }
        }

        // 2. Sprawdź konfigurację (UserToken)
        var userToken = _wykopOptions.UserToken;
        if (!string.IsNullOrWhiteSpace(userToken) && !userToken.Contains("PLACEHOLDER"))
        {
            _memoryCache.Set(UserTokenCacheKey, userToken.Trim(), TimeSpan.FromHours(12));
            return userToken.Trim();
        }

        // 3. Sprawdź RefreshToken
        var refreshToken = _memoryCache.TryGetValue(RefreshTokenCacheKey, out string? cachedRefresh) && !string.IsNullOrWhiteSpace(cachedRefresh)
            ? cachedRefresh
            : _wykopOptions.RefreshToken;

        if (!string.IsNullOrWhiteSpace(refreshToken) && !refreshToken.Contains("PLACEHOLDER"))
        {
            var refreshed = await RefreshUserTokenAsync(refreshToken.Trim(), cancellationToken);
            if (!string.IsNullOrWhiteSpace(refreshed))
            {
                return refreshed;
            }
        }

        return null;
    }

    private async Task<string?> RefreshUserTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Odświeżanie tokenu użytkownika Wykop v3 przez /refresh-token...");
        var baseUrl = GetBaseUrl();
        var client = _httpClientFactory.CreateClient("WykopClient");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "refresh-token"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            data = new
            {
                refresh_token = refreshToken
            }
        });

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Nie udało się odświeżyć tokenu Wykop (HTTP {StatusCode})", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("token", out var tokenEl))
        {
            var newToken = tokenEl.GetString();
            if (!string.IsNullOrWhiteSpace(newToken))
            {
                string? newRefresh = null;
                if (dataEl.TryGetProperty("refresh_token", out var rTokenEl))
                {
                    newRefresh = rTokenEl.GetString();
                }

                await SaveUserTokensAsync(newToken, newRefresh ?? refreshToken, cancellationToken);
                return newToken;
            }
        }

        return null;
    }

    private async Task<string?> GetOrRefreshAppTokenAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(AppTokenCacheKey, out string? cachedToken) && !string.IsNullOrWhiteSpace(cachedToken))
        {
            return cachedToken;
        }

        var key = _wykopOptions.Key ?? _wykopOptions.Token;
        var secret = _wykopOptions.Secret;

        if (string.IsNullOrWhiteSpace(key) || key.Contains("PLACEHOLDER") ||
            string.IsNullOrWhiteSpace(secret) || secret.Contains("PLACEHOLDER"))
        {
            return null;
        }

        var authUrl = _wykopOptions.AuthUrl;
        var client = _httpClientFactory.CreateClient("WykopClient");
        using var authRequest = new HttpRequestMessage(HttpMethod.Post, authUrl);
        authRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        authRequest.Content = JsonContent.Create(new
        {
            data = new
            {
                key = key.Trim(),
                secret = secret.Trim()
            }
        });

        using var authResponse = await client.SendAsync(authRequest, cancellationToken);
        if (!authResponse.IsSuccessStatusCode)
        {
            var errorBody = await authResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd autoryzacji Wykop API v3 (HTTP {StatusCode}): {Error}", authResponse.StatusCode, errorBody);
            throw new HttpRequestException($"Błąd autoryzacji Wykop API v3: {errorBody}");
        }

        var authJson = await authResponse.Content.ReadAsStringAsync(cancellationToken);
        using var authDoc = JsonDocument.Parse(authJson);

        if (authDoc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("token", out var tokenEl))
        {
            var token = tokenEl.GetString();
            if (!string.IsNullOrWhiteSpace(token))
            {
                _memoryCache.Set(AppTokenCacheKey, token, TimeSpan.FromHours(12));
                return token;
            }
        }

        throw new InvalidOperationException("Odpowiedź z /api/v3/auth nie zawierała pola data.token.");
    }

    private string GetBaseUrl()
    {
        var baseUrl = _wykopOptions.BaseUrl;
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        return baseUrl;
    }
}

