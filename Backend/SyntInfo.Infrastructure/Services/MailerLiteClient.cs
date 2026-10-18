using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.Infrastructure.Services;

public class MailerLiteClient : IMailerLiteClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MailerLiteClient> _logger;

    public MailerLiteClient(HttpClient httpClient, IConfiguration configuration, ILogger<MailerLiteClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        var baseUrl = _configuration["SocialPlatforms:MailerLite:BaseUrl"] ?? "https://connect.mailerlite.com/api/";
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public async Task<string> SendNewsletterCampaignAsync(string subject, string htmlContent, string plainTextContent, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["SocialPlatforms:MailerLite:ApiKey"];
        var fromEmail = _configuration["SocialPlatforms:MailerLite:FromEmail"] ?? "newsletter@syntinfo.local";
        var fromName = _configuration["SocialPlatforms:MailerLite:FromName"] ?? "SyntInfo";
        var isMock = _configuration.GetValue("SocialPlatforms:MailerLite:UseMock", false);

        if (isMock || string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("MailerLiteClient [MOCK/TEST]: Tworzenie i wysyłka kampanii (Temat: {Subject})", subject);
            return $"mailerlite-mock-{Guid.NewGuid():N}[..8]";
        }

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Krok 1: Tworzenie kampanii
        var campaignName = $"SyntInfo - {DateTime.UtcNow:yyyy-MM-dd HH:mm} - {subject}";
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

        var createResponse = await _httpClient.PostAsJsonAsync("campaigns", createPayload, cancellationToken);

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

        var scheduleResponse = await _httpClient.PostAsJsonAsync($"campaigns/{campaignId}/schedule", schedulePayload, cancellationToken);
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
        var apiKey = _configuration["SocialPlatforms:MailerLite:ApiKey"];
        var isMock = _configuration.GetValue("SocialPlatforms:MailerLite:UseMock", false);

        if (isMock || string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("MailerLiteClient [MOCK/TEST]: Zapis do newslettera dla {Email}", email);
            return true;
        }

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            email = email.Trim().ToLowerInvariant(),
            fields = string.IsNullOrWhiteSpace(name) ? null : new { name = name.Trim() },
            status = "active"
        };

        var response = await _httpClient.PostAsJsonAsync("subscribers", payload, cancellationToken);
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
        var apiKey = _configuration["SocialPlatforms:MailerLite:ApiKey"];
        var isMock = _configuration.GetValue("SocialPlatforms:MailerLite:UseMock", false);

        if (isMock || string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("MailerLiteClient [MOCK/TEST]: Wypis z newslettera dla {Email}", email);
            return true;
        }

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var payload = new
        {
            email = normalizedEmail,
            status = "unsubscribed"
        };

        var response = await _httpClient.PostAsJsonAsync("subscribers", payload, cancellationToken);
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
