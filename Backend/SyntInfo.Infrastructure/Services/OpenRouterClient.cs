using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Llm;

namespace SyntInfo.Infrastructure.Services
{
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

        public OpenRouterClient(HttpClient httpClient, IConfiguration configuration, ILogger<OpenRouterClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["OpenRouter:ApiKey"] ?? string.Empty;
            _analystModel = configuration["OpenRouter:AnalystModel"] ?? "qwen/qwen-2.5-72b-instruct:free";
            _editorModel = configuration["OpenRouter:EditorModel"] ?? "mistralai/mistral-small-24b-instruct-2501:free";
            _fallbackModel = configuration["OpenRouter:FallbackModel"] ?? "google/gemini-2.0-flash-lite-preview-02-05:free";
            _embeddingModel = configuration["OpenRouter:EmbeddingModel"] ?? "openai/text-embedding-3-small";

            var baseUrl = configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1/";
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
            const string systemPrompt = "Jesteś ekspertem analizy danych i analitykiem informacji. Twoim zadaniem jest wyciągnięcie kluczowych faktów z dostarczonych materiałów (RSS i wyniki wyszukiwania).\n\n" +
                                        "Zasady:\n" +
                                        "1. Zidentyfikuj co najmniej 5-8 najważniejszych faktów.\n" +
                                        "2. Wyeliminuj duplikaty i sprzeczne informacje.\n" +
                                        "3. Zwroc dane WYŁĄCZNIE w formacie JSON jako tablicę obiektów: [{\"fact\": \"treść faktu\", \"source\": \"krótki opis źródła\"}].\n" +
                                        "4. Nie dodawaj wstępu ani zakończenia.";

            var userPrompt = $"[DANE RSS]:\n{rssContent}\n\n[DANE Z WYSZUKIWARKI]:\n{searchContent}";

            _logger.LogInformation("Krok 1: Generowanie faktów za pomocą modelu {Model}", _analystModel);
            return await CallOpenRouterWithRetryAsync(_analystModel, systemPrompt, userPrompt, true, cancellationToken);
        }

        public async Task<string> GenerateSummaryFromFactsAsync(string factsJson, CancellationToken cancellationToken = default)
        {
            const string systemPrompt = "Jesteś doświadczonym redaktorem i architektem informacji. Twoim zadaniem jest stworzenie minimalistycznego, treściwego podsumowania w języku polskim (tzw. Infopiguła) na podstawie listy faktów.\n\n" +
                                        "Zasady:\n" +
                                        "1. TYTUŁ: Stwórz faktyczny, neutralny nagłówek w języku polskim.\n" +
                                        "2. PODSUMOWANIE: Stwórz 3-4 konkretne akapity (łącznie 400-800 znaków).\n" +
                                        "3. KATEGORIA: Przypisz jedną kategorię (np. BIZNES, POLITYKA, TECH, ŚWIAT, PL).\n" +
                                        "4. FORMAT: Zwróć WYŁĄCZNIE czysty, poprawny obiekt JSON: {\"title\": \"...\", \"essence\": \"...\", \"category\": \"...\"}.\n" +
                                        "5. JĘZYK: Odpowiedź MUSI być w języku polskim, niezależnie od języka tekstu źródłowego.\n" +
                                        "Ważne: Wynik musi być poprawnym JSON-em z podwójnymi cudzysłowami (\"). Ewentualne cudzysłowy wewnątrz tekstu muszą być poprawnie eskapowane (\\\"). Nie używaj apostrofów jako zamienników podwójnych cudzysłowów w strukturze JSON.";

            _logger.LogInformation("Krok 2: Generowanie podsumowania za pomocą modelu {Model}", _editorModel);
            return await CallOpenRouterWithRetryAsync(_editorModel, systemPrompt, factsJson, true, cancellationToken);
        }

        public async Task<List<int>> SelectTopArticlesIndexesAsync(string articlesListJson, int expectedCount, Domain.Entities.SourceRegion region, CancellationToken cancellationToken = default)
        {
            string regionCriteria = region == Domain.Entities.SourceRegion.Poland ? "o znaczeniu dla obywatela Polski" : "znaczeniem międzynarodowym";
            var systemPrompt = $"Jesteś asystentem redakcyjnym. Otrzymujesz w formacie JSON (array) listę dostępnych najnowszych artykułów informacyjnych z ich indeksami, tytułami i opisami. Twoim zadaniem jest wskazanie dokładnie {expectedCount} indeksów NAJWAŻNIEJSZYCH tekstów z tej listy. Kieruj się skalą problemu, {regionCriteria} i siłą oddziaływania społecznego lub gospodarczego. Zwroc WYŁĄCZNIE czysty, poprawny obiekt JSON w formacie: {{\"selectedIndexes\": [0, 1, 3, ...]}}. Nie dodawaj innych tłumaczeń ani komentarzy.";

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
}
