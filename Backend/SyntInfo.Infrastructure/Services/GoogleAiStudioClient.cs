using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SyntInfo.Application.Prompts;

namespace SyntInfo.Infrastructure.Services;

public class GoogleAiStudioClient : IGoogleAiStudioClient
{
    private readonly HttpClient _httpClient;
    private readonly IOpenRouterClient _openRouterClient;
    private readonly ILogger<GoogleAiStudioClient> _logger;
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly string _baseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public GoogleAiStudioClient(HttpClient httpClient, IOpenRouterClient openRouterClient, IConfiguration configuration, ILogger<GoogleAiStudioClient> logger)
    {
        _httpClient = httpClient;
        _openRouterClient = openRouterClient;
        _logger = logger;
        _apiKey = configuration["GoogleAiStudio:ApiKey"] ?? string.Empty;
        _modelName = configuration["GoogleAiStudio:Model"] ?? "gemini-2.5-flash";
    }

    public async Task<List<int>> SelectTopArticlesIndexesAsync(string articlesListJson, int expectedCount, Domain.Entities.SourceRegion region, CancellationToken cancellationToken = default)
    {
        var systemPrompt = AiPrompts.ArticleSelection.BuildSystemPrompt(expectedCount, region);

        try
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "PLACEHOLDER_GOOGLE_KEY")
            {
                return await _openRouterClient.SelectTopArticlesIndexesAsync(articlesListJson, expectedCount, region, cancellationToken);
            }

            _logger.LogInformation("Krok 1: Wybór artykułów za pomocą Google AI Studio (Model: {Model})", _modelName);

            var responseContent = await CallGeminiAsync(systemPrompt, articlesListJson, true, cancellationToken);

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                return await _openRouterClient.SelectTopArticlesIndexesAsync(articlesListJson, expectedCount, region, cancellationToken);
            }

            using var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("selectedIndexes", out var arrayEl))
            {
                var list = new List<int>();
                foreach (var el in arrayEl.EnumerateArray())
                {
                    if (el.TryGetInt32(out int val)) list.Add(val);
                }
                return list;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd w GoogleAiStudioClient.SelectTopArticlesIndexesAsync. Przełączanie na OpenRouter.");
            return await _openRouterClient.SelectTopArticlesIndexesAsync(articlesListJson, expectedCount, region, cancellationToken);
        }

        return new List<int>();
    }

    public async Task<string> GenerateFactsAsync(string content, string searchContent, CancellationToken cancellationToken = default)
    {
        var systemPrompt = AiPrompts.FactExtraction.SystemPrompt;
        var userPrompt = AiPrompts.FactExtraction.BuildUserPrompt(content, searchContent);

        try
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "PLACEHOLDER_GOOGLE_KEY")
            {
                return await _openRouterClient.GenerateFactsAsync(content, searchContent, cancellationToken);
            }

            _logger.LogInformation("Krok 1: Generowanie faktów za pomocą Google AI Studio (Model: {Model})", _modelName);
            return await CallGeminiAsync(systemPrompt, userPrompt, true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd w GoogleAiStudioClient.GenerateFactsAsync. Przełączanie na OpenRouter.");
            return await _openRouterClient.GenerateFactsAsync(content, searchContent, cancellationToken);
        }
    }

    public async Task<string> GenerateSummaryFromFactsAsync(string factsJson, CancellationToken cancellationToken = default)
    {
        var systemPrompt = AiPrompts.Summary.FromFactsSystemPrompt;

        try
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "PLACEHOLDER_GOOGLE_KEY")
            {
                _logger.LogWarning("Brak klucza API dla Google AI Studio lub klucz jest domyślny. Przełączanie na OpenRouter.");
                return await _openRouterClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
            }

            _logger.LogInformation("Krok 2: Generowanie podsumowania za pomocą Google AI Studio (Model: {Model})", _modelName);
            return await CallGeminiAsync(systemPrompt, factsJson, true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wystąpił nieoczekiwany błąd w GoogleAiStudioClient. Przełączanie na OpenRouter.");
            return await _openRouterClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
        }
    }

    private async Task<string> CallGeminiAsync(string systemPrompt, string userPrompt, bool forceJson, CancellationToken cancellationToken)
    {
        var requestUri = $"{_baseUrl}/{_modelName}:generateContent?key={_apiKey}";

        var payloadObject = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.3,
                responseMimeType = forceJson ? "application/json" : "text/plain"
            }
        };

        int maxRetries = 3;
        int delayMs = 2000;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(requestUri, payloadObject, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt == maxRetries)
                    {
                        _logger.LogWarning("Google AI Studio: Przekroczono limit zapytań (429 Too Many Requests) po {Attempt} próbach.", attempt + 1);
                        throw new Exception("Google AI Studio Rate Limit");
                    }

                    _logger.LogWarning("Google AI Studio: Otrzymano status 429. Próba {Attempt}/{Max}. Czekam {Delay}ms...", attempt + 1, maxRetries + 1, delayMs);
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Błąd Google AI Studio API ({StatusCode}). Szczegóły: {Error}", response.StatusCode, error);

                    // Retry for transient 5xx server errors
                    if ((int)response.StatusCode >= 500 && attempt < maxRetries)
                    {
                        _logger.LogWarning("Google AI Studio: Otrzymano status {StatusCode}. Próba {Attempt}/{Max}. Czekam {Delay}ms...", response.StatusCode, attempt + 1, maxRetries + 1, delayMs);
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs *= 2;
                        continue;
                    }

                    throw new Exception($"Google AI Studio API error: {response.StatusCode}");
                }

                var resultJson = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(resultJson);

                if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var content = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return content;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex) when (attempt < maxRetries && (ex is HttpRequestException || ex is TaskCanceledException || ex.Message.Contains("Rate Limit")))
            {
                _logger.LogWarning("Błąd połączenia z Google AI Studio: {Message}. Próba {Attempt}/{Max}. Czekam {Delay}ms...", ex.Message, attempt + 1, maxRetries + 1, delayMs);
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        return string.Empty;
    }

}
