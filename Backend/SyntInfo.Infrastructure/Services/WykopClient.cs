using System.Net;
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
    private static readonly SemaphoreSlim _fileLock = new(1, 1);

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

    private static string GetTokenFilePath()
    {
        var logsPath = Path.Combine(AppContext.BaseDirectory, "logs", "wykop_tokens.json");
        var basePath = Path.Combine(AppContext.BaseDirectory, "wykop_tokens.json");

        // Jeśli plik istnieje w głównym katalogu (dla wstecznej kompatybilności), użyj go, w przeciwnym razie wybierz logs/
        return File.Exists(basePath) ? basePath : logsPath;
    }

    private async Task<WykopTokenData?> ReadTokenFileAsync(CancellationToken cancellationToken)
    {
        var tokenPath = GetTokenFilePath();
        if (!File.Exists(tokenPath))
        {
            return null;
        }

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(tokenPath))
            {
                return null;
            }

            var fileContent = await File.ReadAllTextAsync(tokenPath, cancellationToken);
            return JsonSerializer.Deserialize<WykopTokenData>(fileContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd odczytu tokenów Wykop z pliku {Path}", tokenPath);
            return null;
        }
        finally
        {
            _fileLock.Release();
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

        var tokenPath = GetTokenFilePath();
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var dir = Path.GetDirectoryName(tokenPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var data = new WykopTokenData
            {
                UserToken = userToken.Trim(),
                RefreshToken = refreshToken?.Trim(),
                SavedAt = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(tokenPath, json, cancellationToken);
            _logger.LogInformation("Zapisano trwały token użytkownika Wykop w pliku {Path}", tokenPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się zapisać tokenów Wykop do pliku {Path}", tokenPath);
        }
        finally
        {
            _fileLock.Release();
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

            var (success, key, isAuthError) = await ExecuteUploadRequestAsync(wykopClient, baseUrl, imageBytes, fileName, userToken, cancellationToken);
            if (success && key != null)
            {
                return key;
            }

            // Jeśli wysłanie nie powiodło się z powodu błędu autoryzacji (401/403), odświeżamy token i ponawiamy
            if (isAuthError)
            {
                _logger.LogWarning("Błąd autoryzacji podczas wysyłania zdjęcia do Wykopu. Próba automatycznego odświeżenia tokenu użytkownika...");
                _memoryCache.Remove(UserTokenCacheKey);
                var refreshedToken = await TryRefreshStoredUserTokenAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(refreshedToken))
                {
                    _logger.LogInformation("Token odświeżony pomyślnie. Ponawianie wysyłki zdjęcia...");
                    var retryResult = await ExecuteUploadRequestAsync(wykopClient, baseUrl, imageBytes, fileName, refreshedToken, cancellationToken);
                    if (retryResult.Success && retryResult.Key != null)
                    {
                        return retryResult.Key;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wystąpił błąd podczas przesyłania zdjęcia do Wykopu: {Url}", imageUrlOrPath);
        }

        return null;
    }

    private async Task<(bool Success, string? Key, bool IsAuthError)> ExecuteUploadRequestAsync(
        HttpClient wykopClient,
        string baseUrl,
        byte[] imageBytes,
        string fileName,
        string userToken,
        CancellationToken cancellationToken)
    {
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
            var isAuth = uploadResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            _logger.LogWarning("Nie udało się wysłać zdjęcia do Wykop v3 (HTTP {StatusCode}): {Error}", uploadResponse.StatusCode, err);
            return (false, null, isAuth);
        }

        var json = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("key", out var keyEl))
        {
            var key = keyEl.GetString();
            _logger.LogInformation("Pomyślnie wysłano zdjęcie do Wykopu. Klucz zdjęcia: {Key}", key);
            return (true, key, false);
        }

        return (false, null, false);
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

        var response = await SendEntryRequestAsync(client, baseUrl, content, photoKey, userToken, cancellationToken);

        // Jeśli status to 401 lub 403, spróbujmy odświeżyć token i ponowić zapytanie
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Wykop zwrócił status {StatusCode} ({Error}) podczas publikacji wpisu. Próba automatycznego odświeżenia tokenu użytkownika...", response.StatusCode, errorBody);

            _memoryCache.Remove(UserTokenCacheKey);
            var refreshedToken = await TryRefreshStoredUserTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(refreshedToken))
            {
                _logger.LogInformation("Token użytkownika odświeżony pomyślnie. Ponawianie publikacji wpisu na Wykopie...");
                response.Dispose();
                response = await SendEntryRequestAsync(client, baseUrl, content, photoKey, refreshedToken, cancellationToken);
            }
        }

        using (response)
        {
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

    private static async Task<HttpResponseMessage> SendEntryRequestAsync(
        HttpClient client,
        string baseUrl,
        string content,
        string? photoKey,
        string token,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "entries"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            data = new
            {
                content,
                photo = photoKey
            }
        });

        return await client.SendAsync(request, cancellationToken);
    }

    private async Task<string?> GetOrRefreshUserTokenAsync(CancellationToken cancellationToken)
    {
        // 1. Sprawdź pamięć podręczną
        if (_memoryCache.TryGetValue(UserTokenCacheKey, out string? cachedUserToken) && !string.IsNullOrWhiteSpace(cachedUserToken))
        {
            return cachedUserToken;
        }

        // 1b. Sprawdź trwały plik wykop_tokens.json
        var fileData = await ReadTokenFileAsync(cancellationToken);
        if (fileData != null)
        {
            if (!string.IsNullOrWhiteSpace(fileData.RefreshToken))
            {
                _memoryCache.Set(RefreshTokenCacheKey, fileData.RefreshToken.Trim(), TimeSpan.FromDays(30));
            }

            if (!string.IsNullOrWhiteSpace(fileData.UserToken))
            {
                // Sprawdź wiek tokenu (jeśli SavedAt jest obecne)
                if (fileData.SavedAt.HasValue)
                {
                    var tokenAge = DateTime.UtcNow - fileData.SavedAt.Value;
                    if (tokenAge > TimeSpan.FromHours(23))
                    {
                        _logger.LogInformation("Token użytkownika Wykop w pliku wygasł ({AgeHours:F1}h temu). Próba automatycznego odświeżenia za pomocą RefreshToken...", tokenAge.TotalHours);
                        if (!string.IsNullOrWhiteSpace(fileData.RefreshToken))
                        {
                            var refreshed = await RefreshUserTokenAsync(fileData.RefreshToken.Trim(), cancellationToken);
                            if (!string.IsNullOrWhiteSpace(refreshed))
                            {
                                return refreshed;
                            }
                        }
                    }
                    else
                    {
                        var remainingTtl = TimeSpan.FromHours(23) - tokenAge;
                        _memoryCache.Set(UserTokenCacheKey, fileData.UserToken.Trim(), remainingTtl);
                        return fileData.UserToken.Trim();
                    }
                }
                else
                {
                    // Starszy format bez SavedAt - cache'uj na 1h
                    _memoryCache.Set(UserTokenCacheKey, fileData.UserToken.Trim(), TimeSpan.FromHours(1));
                    return fileData.UserToken.Trim();
                }
            }
        }

        // 2. Sprawdź konfigurację (UserToken)
        var userToken = _wykopOptions.UserToken;
        if (!string.IsNullOrWhiteSpace(userToken) && !userToken.Contains("PLACEHOLDER"))
        {
            _memoryCache.Set(UserTokenCacheKey, userToken.Trim(), TimeSpan.FromHours(12));
            return userToken.Trim();
        }

        // 3. Sprawdź RefreshToken z cache, pliku lub konfiguracji
        return await TryRefreshStoredUserTokenAsync(cancellationToken);
    }

    private async Task<string?> TryRefreshStoredUserTokenAsync(CancellationToken cancellationToken)
    {
        string? refreshToken = null;
        if (_memoryCache.TryGetValue(RefreshTokenCacheKey, out string? cachedRefresh) && !string.IsNullOrWhiteSpace(cachedRefresh))
        {
            refreshToken = cachedRefresh.Trim();
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            var fileData = await ReadTokenFileAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(fileData?.RefreshToken))
            {
                refreshToken = fileData.RefreshToken.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(refreshToken) && !string.IsNullOrWhiteSpace(_wykopOptions.RefreshToken) && !_wykopOptions.RefreshToken.Contains("PLACEHOLDER"))
        {
            refreshToken = _wykopOptions.RefreshToken.Trim();
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            _logger.LogWarning("Brak tokena odświeżania (RefreshToken) Wykop.");
            return null;
        }

        return await RefreshUserTokenAsync(refreshToken, cancellationToken);
    }

    private async Task<string?> RefreshUserTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Odświeżanie tokenu użytkownika Wykop v3 przez /refresh-token...");
        var baseUrl = GetBaseUrl();
        var client = _httpClientFactory.CreateClient("WykopClient");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "refresh-token"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var appToken = await GetOrRefreshAppTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(appToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appToken);
        }

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
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Nie udało się odświeżyć tokenu Wykop (HTTP {StatusCode}): {Error}", response.StatusCode, err);
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
                _logger.LogInformation("Pomyślnie odświeżono i zapisano nowy token użytkownika Wykop.");
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

    private sealed class WykopTokenData
    {
        public string? UserToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? SavedAt { get; set; }
    }
}


