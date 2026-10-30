using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SyntInfo.Infrastructure.Services;

public class MailerLiteClient : IMailerLiteClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MailerLiteOptions _mailerLiteOptions;
    private readonly ILogger<MailerLiteClient> _logger;

    public MailerLiteClient(IHttpClientFactory httpClientFactory, IOptions<MailerLiteOptions> mailerLiteOptions, ILogger<MailerLiteClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _mailerLiteOptions = mailerLiteOptions.Value;
        _logger = logger;
    }

    private Uri GetBaseUri()
    {
        var baseUrl = _mailerLiteOptions.BaseUrl;
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        return new Uri(baseUrl);
    }

    public async Task<string> SendNewsletterCampaignAsync(string subject, string htmlContent, string plainTextContent, CancellationToken cancellationToken = default)
    {
        var apiKey = _mailerLiteOptions.ApiKey;
        var fromEmail = _mailerLiteOptions.FromEmail;
        var fromName = _mailerLiteOptions.FromName;
        var isMock = _mailerLiteOptions.UseMock;

        if (isMock || string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("MailerLiteClient [MOCK/TEST]: Tworzenie i wysyłka kampanii (Temat: {Subject})", subject);
            return $"mailerlite-mock-{Guid.NewGuid():N}"[..24];
        }

        var client = _httpClientFactory.CreateClient("MailerLiteClient");
        var baseUri = GetBaseUri();

        // Krok 1: Tworzenie kampanii
        var campaignName = $"InfoSkrót - {DateTime.UtcNow:yyyy-MM-dd HH:mm} - {subject}";
        if (campaignName.Length > 250) campaignName = campaignName[..250];

        var createPayload = new
        {
            name = campaignName,
            type = "regular",
            emails = new[]
            {
                new
                {
                    subject = subject.Length > 250 ? subject[..250] : subject,
                    from_name = fromName,
                    from = fromEmail,
                    content = htmlContent
                }
            }
        };

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "campaigns"));
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        createRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        createRequest.Content = JsonContent.Create(createPayload);

        using var createResponse = await client.SendAsync(createRequest, cancellationToken);

        if (!createResponse.IsSuccessStatusCode)
        {
            var err = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd tworzenia kampanii w MailerLite (HTTP {StatusCode}): {Error}", createResponse.StatusCode, err);
            throw new HttpRequestException($"MailerLite API błąd tworzenia kampanii: {err}");
        }

        var createJson = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        using var createDoc = JsonDocument.Parse(createJson);

        string? campaignId = null;
        if (createDoc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("id", out var idEl))
        {
            campaignId = idEl.GetString();
        }

        if (string.IsNullOrWhiteSpace(campaignId))
        {
            throw new InvalidOperationException("Nie udało się uzyskać ID kampanii z odpowiedzi MailerLite.");
        }

        // Krok 2: Natychmiastowa wysyłka (instant delivery)
        var schedulePayload = new
        {
            delivery = "instant"
        };

        using var scheduleRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"campaigns/{campaignId}/schedule"));
        scheduleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        scheduleRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        scheduleRequest.Content = JsonContent.Create(schedulePayload);

        using var scheduleResponse = await client.SendAsync(scheduleRequest, cancellationToken);

        if (!scheduleResponse.IsSuccessStatusCode)
        {
            var scheduleErr = await scheduleResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd harmonogramowania kampanii w MailerLite {CampaignId}: {Error}", campaignId, scheduleErr);
            throw new HttpRequestException($"MailerLite API błąd harmonogramowania kampanii: {scheduleErr}");
        }

        _logger.LogInformation("Wysłano kampanię newslettera MailerLite o ID: {CampaignId}", campaignId);
        return campaignId;
    }

    public async Task<bool> SubscribeAsync(string email, string? name = null, CancellationToken cancellationToken = default)
    {
        var apiKey = _mailerLiteOptions.ApiKey;
        var isMock = _mailerLiteOptions.UseMock;

        if (isMock || string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("MailerLiteClient [MOCK/TEST]: Zapis do newslettera dla {Email}", email);
            return true;
        }

        var client = _httpClientFactory.CreateClient("MailerLiteClient");
        var baseUri = GetBaseUri();

        var payload = new
        {
            email = email.Trim().ToLowerInvariant(),
            fields = string.IsNullOrWhiteSpace(name) ? null : new { name = name.Trim() },
            status = "active"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "subscribers"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd zapisu subskrybenta w MailerLite {Email} (HTTP {StatusCode}): {Error}", email, response.StatusCode, err);
            return false;
        }

        _logger.LogInformation("Pomyślnie dodano/zaktualizowano subskrybenta w MailerLite: {Email}", email);
        return true;
    }

    public async Task<bool> UnsubscribeAsync(string email, CancellationToken cancellationToken = default)
    {
        var apiKey = _mailerLiteOptions.ApiKey;
        var isMock = _mailerLiteOptions.UseMock;

        if (isMock || string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("MailerLiteClient [MOCK/TEST]: Wypis z newslettera dla {Email}", email);
            return true;
        }

        var client = _httpClientFactory.CreateClient("MailerLiteClient");
        var baseUri = GetBaseUri();

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var payload = new
        {
            email = normalizedEmail,
            status = "unsubscribed"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "subscribers"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Błąd wypisu subskrybenta w MailerLite {Email} (HTTP {StatusCode}): {Error}", email, response.StatusCode, err);
            return false;
        }

        _logger.LogInformation("Pomyślnie wypisano subskrybenta z MailerLite: {Email}", email);
        return true;
    }
}
