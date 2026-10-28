using System.Net.Http.Json;
using System.Text.Json;
using SyntInfo.Application.Prompts;

namespace SyntInfo.Infrastructure.Services;

public class GoogleAiStudioClient : IGoogleAiStudioClient
{
    private readonly HttpClient _httpClient;
    private readonly IOpenRouterClient _openRouterClient;
    private readonly ILogger<GoogleAiStudioClient> _logger;
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly string _fallbackModelName;
    private readonly string _baseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public GoogleAiStudioClient(HttpClient httpClient, IOpenRouterClient openRouterClient, IOptions<GoogleAiStudioOptions> googleAiStudioOptions, ILogger<GoogleAiStudioClient> logger)
    {
        _httpClient = httpClient;
        _openRouterClient = openRouterClient;
        _logger = logger;
        _apiKey = googleAiStudioOptions.Value.ApiKey;
        _modelName = !string.IsNullOrWhiteSpace(googleAiStudioOptions.Value.Model) ? googleAiStudioOptions.Value.Model : "gemini-3.5-flash";
        _fallbackModelName = !string.IsNullOrWhiteSpace(googleAiStudioOptions.Value.FallbackModel) ? googleAiStudioOptions.Value.FallbackModel : "gemini-3.1-flash-lite";
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
                if (cancellationToken.IsCancellationRequested)
                {
                    return new List<int>();
                }
                _logger.LogWarning("Brak odpowiedzi z Google AI Studio. Przełączanie na OpenRouter.");
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
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Błąd w GoogleAiStudioClient.SelectTopArticlesIndexesAsync. Przełączanie na OpenRouter.");
            try
            {
                return await _openRouterClient.SelectTopArticlesIndexesAsync(articlesListJson, expectedCount, region, cancellationToken);
            }
            catch (Exception orEx)
            {
                _logger.LogError(orEx, "Fallback do OpenRouter w SelectTopArticlesIndexesAsync również nie powiódł się.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Przerwano zapytanie do Google AI Studio (anulowano token): {Message}", ex.Message);
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
            var result = await CallGeminiAsync(systemPrompt, userPrompt, true, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result))
            {
                return result;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Pusta odpowiedź z Google AI Studio w GenerateFactsAsync. Przełączanie na OpenRouter.");
                return await _openRouterClient.GenerateFactsAsync(content, searchContent, cancellationToken);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Błąd w GoogleAiStudioClient.GenerateFactsAsync. Przełączanie na OpenRouter.");
            try
            {
                return await _openRouterClient.GenerateFactsAsync(content, searchContent, cancellationToken);
            }
            catch (Exception orEx)
            {
                _logger.LogError(orEx, "Fallback do OpenRouter w GenerateFactsAsync również nie powiódł się.");
            }
        }

        return string.Empty;
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
            var result = await CallGeminiAsync(systemPrompt, factsJson, true, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result))
            {
                return result;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Pusta odpowiedź z Google AI Studio w GenerateSummaryFromFactsAsync. Przełączanie na OpenRouter.");
                return await _openRouterClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Wystąpił nieoczekiwany błąd w GoogleAiStudioClient. Przełączanie na OpenRouter.");
            try
            {
                return await _openRouterClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
            }
            catch (Exception orEx)
            {
                _logger.LogError(orEx, "Fallback do OpenRouter w GenerateSummaryFromFactsAsync również nie powiódł się.");
            }
        }

        return string.Empty;
    }

    private async Task<string> CallGeminiAsync(string systemPrompt, string userPrompt, bool forceJson, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return string.Empty;
        }

        // 1. Próba z modelem głównym
        try
        {
            var primaryResult = await CallGeminiModelWithRetryAsync(_modelName, systemPrompt, userPrompt, forceJson, cancellationToken);
            if (!string.IsNullOrWhiteSpace(primaryResult))
            {
                return primaryResult;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && !string.IsNullOrWhiteSpace(_fallbackModelName) && _fallbackModelName != _modelName)
        {
            _logger.LogWarning("Model Google AI Studio {PrimaryModel} nie powiódł się ({Message}). Przełączanie na model rezerwowy: {FallbackModel}",
                _modelName, ex.Message, _fallbackModelName);
        }

        // 2. Wewnętrzny fallback na model rezerwowy w Google AI Studio (jeśli główny zawiódł)
        if (!cancellationToken.IsCancellationRequested && !string.IsNullOrWhiteSpace(_fallbackModelName) && _fallbackModelName != _modelName)
        {
            try
            {
                _logger.LogInformation("Próba wywołania modelu rezerwowego Google AI Studio (Model: {FallbackModel})", _fallbackModelName);
                var fallbackResult = await CallGeminiModelWithRetryAsync(_fallbackModelName, systemPrompt, userPrompt, forceJson, cancellationToken);
                if (!string.IsNullOrWhiteSpace(fallbackResult))
                {
                    return fallbackResult;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Model rezerwowy Google AI Studio {FallbackModel} również nie powiódł się: {Message}", _fallbackModelName, ex.Message);
            }
        }

        return string.Empty;
    }

    private async Task<string> CallGeminiModelWithRetryAsync(string modelName, string systemPrompt, string userPrompt, bool forceJson, CancellationToken cancellationToken)
    {
        var requestUri = $"{_baseUrl}/{modelName}:generateContent?key={_apiKey}";

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

        int maxRetries = 1;
        int delayMs = 1500;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            try
            {
                var response = await _httpClient.PostAsJsonAsync(requestUri, payloadObject, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt == maxRetries)
                    {
                        _logger.LogWarning("Google AI Studio ({Model}): Przekroczono limit zapytań (429) po {Attempt} próbach.", modelName, attempt + 1);
                        throw new Exception($"Google AI Studio Rate Limit for model {modelName}");
                    }

                    _logger.LogWarning("Google AI Studio ({Model}): Status 429. Próba {Attempt}/{Max}. Czekam {Delay}ms...", modelName, attempt + 1, maxRetries + 1, delayMs);
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Błąd Google AI Studio API ({StatusCode}) dla modelu {Model}. Szczegóły: {Error}", response.StatusCode, modelName, error);

                    // Ponów próbę raz dla błędów serwera 5xx (np. 503 Service Unavailable / High demand)
                    if ((int)response.StatusCode >= 500 && attempt < maxRetries)
                    {
                        _logger.LogWarning("Google AI Studio ({Model}): Status {StatusCode}. Próba {Attempt}/{Max}. Czekam {Delay}ms...", modelName, response.StatusCode, attempt + 1, maxRetries + 1, delayMs);
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs *= 2;
                        continue;
                    }

                    throw new Exception($"Google AI Studio API error: {response.StatusCode} for model {modelName}");
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
            catch (Exception ex) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested && (ex is HttpRequestException || ex is TaskCanceledException || ex.Message.Contains("Rate Limit")))
            {
                _logger.LogWarning("Błąd połączenia z Google AI Studio ({Model}): {Message}. Próba {Attempt}/{Max}. Czekam {Delay}ms...", modelName, ex.Message, attempt + 1, maxRetries + 1, delayMs);
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        return string.Empty;
    }
}
