using System.Net.Http.Json;
using System.Text.Json;
using SyntInfo.Application.Models.Llm;
using SyntInfo.Application.Prompts;

namespace SyntInfo.Infrastructure.Services;

public class OpenRouterClient : IOpenRouterClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenRouterClient> _logger;
    private readonly string _apiKey;
    private readonly string _analystModel;
    private readonly string _editorModel;
    private readonly string _fallbackModel;
    private readonly string _embeddingModel;

    // Rate limiting for :free models
    private static readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(1, 1);
    private static DateTime _lastRequestTime = DateTime.MinValue;
    private const int MinIntervalMs = 12000; // Increased to 12 seconds (~5 RPM) for better stability

    public OpenRouterClient(HttpClient httpClient, IOptions<OpenRouterOptions> openRouterOptions, ILogger<OpenRouterClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        var options = openRouterOptions.Value;
        _apiKey = options.ApiKey;
        _analystModel = !string.IsNullOrWhiteSpace(options.AnalystModel) ? options.AnalystModel : "google/gemma-4-31b-it:free";
        _editorModel = !string.IsNullOrWhiteSpace(options.EditorModel) ? options.EditorModel : "meta-llama/llama-4-maverick:free";
        _fallbackModel = !string.IsNullOrWhiteSpace(options.FallbackModel) ? options.FallbackModel : "openrouter/free";
        _embeddingModel = !string.IsNullOrWhiteSpace(options.EmbeddingModel) ? options.EmbeddingModel : "openai/text-embedding-3-small";

        var baseUrl = options.BaseUrl;
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        _httpClient.BaseAddress = new Uri(baseUrl);

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/dariuszkusz/SyntInfo");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "SyntInfo AI News Aggregator");
        }
    }

    public async Task<string> GenerateFactsAsync(string rssContent, string searchContent, CancellationToken cancellationToken = default)
    {
        var systemPrompt = AiPrompts.FactExtraction.SystemPrompt;
        var userPrompt = AiPrompts.FactExtraction.BuildUserPrompt(rssContent, searchContent);

        _logger.LogInformation("Krok 1: Generowanie faktów za pomocą modelu {Model}", _analystModel);
        return await CallOpenRouterWithRetryAsync(_analystModel, systemPrompt, userPrompt, true, cancellationToken);
    }

    public async Task<string> GenerateSummaryFromFactsAsync(string factsJson, CancellationToken cancellationToken = default)
    {
        var systemPrompt = AiPrompts.Summary.FromFactsSystemPrompt;

        _logger.LogInformation("Krok 2: Generowanie podsumowania za pomocą modelu {Model}", _editorModel);
        return await CallOpenRouterWithRetryAsync(_editorModel, systemPrompt, factsJson, true, cancellationToken);
    }

    public async Task<List<int>> SelectTopArticlesIndexesAsync(string articlesListJson, int expectedCount, Domain.Entities.SourceRegion region, CancellationToken cancellationToken = default)
    {
        var systemPrompt = AiPrompts.ArticleSelection.BuildSystemPrompt(expectedCount, region);

        _logger.LogInformation("Wybór najważniejszych artykułów za pomocą modelu {Model}", _analystModel);
        var responseContent = await CallOpenRouterWithRetryAsync(_analystModel, systemPrompt, articlesListJson, true, cancellationToken);

        try
        {
            var cleanRes = responseContent.Replace("```json", "").Replace("```", "").Trim();
            using var doc = JsonDocument.Parse(cleanRes);
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
            _logger.LogError(ex, "Błąd parsowania wybranych indeksów: {Content}", responseContent);
        }

        return new List<int>();
    }

    private async Task<string> CallOpenRouterWithRetryAsync(string model, string systemPrompt, string userPrompt, bool forceJson, CancellationToken cancellationToken)
    {
        int maxRetries = 3; // Reduced to 3 for primary model
        int delayMs = 12000;
        string currentModel = model;

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                var response = await CallOpenRouterAsync(currentModel, systemPrompt, userPrompt, forceJson, cancellationToken);

                // Validacja JSON jesli wymuszony
                if (forceJson && string.IsNullOrWhiteSpace(response))
                {
                    throw new Exception("Empty response from LLM when JSON was expected.");
                }

                return response;
            }
            catch (Exception ex)
            {
                bool isRateLimit = ex is HttpRequestException hrex && hrex.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
                bool isTimeout = ex.Message.Contains("Timeout") || ex is TaskCanceledException;

                _logger.LogWarning("Błąd modelu {Model} (Próba {Current}/{Max}): {Error}", currentModel, i + 1, maxRetries + 1, ex.Message);

                // Jesli to ostatnia proba lub powazny blad, a nie jestesmy jeszcze na fallbacku - przelacz na fallback
                if ((i >= 1 || isTimeout) && currentModel != _fallbackModel)
                {
                    _logger.LogWarning("Przełączanie na model Fallback: {FallbackModel}", _fallbackModel);
                    currentModel = _fallbackModel;
                    i = 0; // Resetujemy licznik prob dla modelu fallback
                    maxRetries = 2; // Mniej prob dla fallbacka
                    continue;
                }

                if (i == maxRetries) throw;

                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        throw new Exception($"Nie udało się uzyskać odpowiedzi z OpenRouter (Primary: {model}, Fallback: {_fallbackModel}).");
    }

    private async Task<string> CallOpenRouterAsync(string model, string systemPrompt, string userPrompt, bool forceJson, CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var elapsedSinceLast = (now - _lastRequestTime).TotalMilliseconds;
            if (elapsedSinceLast < MinIntervalMs)
            {
                var delay = MinIntervalMs - (int)elapsedSinceLast;
                _logger.LogInformation("Rate limiting: oczekiwanie {Delay}ms przed strzałem do OpenRouter", delay);
                await Task.Delay(delay, cancellationToken);
            }

            var request = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.3,
                response_format = forceJson ? new { type = "json_object" } : null
            };

            var response = await _httpClient.PostAsJsonAsync("chat/completions", request, cancellationToken);
            _lastRequestTime = DateTime.UtcNow;

            LogRateLimitHeaders(response.Headers);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new HttpRequestException("429 TooManyRequests", null, System.Net.HttpStatusCode.TooManyRequests);
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Błąd API OpenRouter ({StatusCode}). Model: {Model}. Szczegóły: {Error}",
                    response.StatusCode, model, error);
                throw new Exception($"OpenRouter API error: {response.StatusCode} (Model: {model})");
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIChatCompletionResponse>(cancellationToken: cancellationToken);

            if (result?.Choices == null || result.Choices.Count == 0)
            {
                _logger.LogWarning("Model {Model} zwrócił pustą listę Choices.", model);
                return string.Empty;
            }

            var content = result.Choices[0].Message.Content ?? string.Empty;

            return content;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Timeout podczas zapytania do OpenRouter (Model: {Model}). Serwer nie odpowiedział w terminie.", model);
            throw new Exception($"OpenRouter Timeout for model {model}", ex);
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    public async Task<float[]> GenerateEmbeddingsAsync(string text, CancellationToken cancellationToken = default)
    {
        int maxRetries = 5;
        int delayMs = 15000;

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                await _rateLimiter.WaitAsync(cancellationToken);
                try
                {
                    var request = new
                    {
                        model = _embeddingModel,
                        input = text
                    };

                    var response = await _httpClient.PostAsJsonAsync("embeddings", request, cancellationToken);
                    _lastRequestTime = DateTime.UtcNow;

                    LogRateLimitHeaders(response.Headers);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        throw new HttpRequestException("429 TooManyRequests", null, System.Net.HttpStatusCode.TooManyRequests);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("Błąd OpenRouter Embeddings ({StatusCode}): {Error}", response.StatusCode, error);
                        return Array.Empty<float>();
                    }

                    using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

                    if (jsonDoc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.GetArrayLength() > 0)
                    {
                        var firstData = dataArray[0];
                        if (firstData.TryGetProperty("embedding", out var embeddingElement))
                        {
                            return JsonSerializer.Deserialize<float[]>(embeddingElement.GetRawText()) ?? Array.Empty<float>();
                        }
                    }

                    return Array.Empty<float>();
                }
                finally
                {
                    _rateLimiter.Release();
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests || ex.Message.Contains("429"))
            {
                if (i == maxRetries) throw;
                _logger.LogWarning("Otrzymano 429 (Embeddings) z OpenRouter. Próba {Current}/{Max}. Czekam {Delay}ms...", i + 1, maxRetries, delayMs);
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wyjątek podczas generowania wektorów w OpenRouter.");
                return Array.Empty<float>();
            }
        }
        return Array.Empty<float>();
    }

    public async Task<string> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("key", cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("OpenRouter Key Usage: {Content}", content);
            }
            else
            {
                _logger.LogWarning("Nie udało się pobrać informacji o użyciu klucza: {StatusCode}", response.StatusCode);
            }

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas pobierania informacji o użyciu klucza.");
            return "Error: " + ex.Message;
        }
    }

    private void LogRateLimitHeaders(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        if (headers.TryGetValues("X-RateLimit-Limit", out var limit))
            _logger.LogInformation("[Rate Limit] Limit: {Limit}", limit.FirstOrDefault());
        if (headers.TryGetValues("X-RateLimit-Remaining", out var remaining))
            _logger.LogInformation("[Rate Limit] Remaining: {Remaining}", remaining.FirstOrDefault());
        if (headers.TryGetValues("X-RateLimit-Reset", out var reset))
            _logger.LogInformation("[Rate Limit] Reset: {Reset}", reset.FirstOrDefault());
    }
}
