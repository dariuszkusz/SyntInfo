using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.Infrastructure.Services
{
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
            _modelName = configuration["GoogleAiStudio:Model"] ?? "gemini-1.5-flash-lite";
        }

        public async Task<string> GenerateSummaryFromFactsAsync(string factsJson, CancellationToken cancellationToken = default)
        {
            const string systemPrompt = "Jesteś doświadczonym redaktorem i architektem informacji. Twoim zadaniem jest stworzenie minimalistycznego, treściwego podsumowania w języku polskim (tzw. Infopiguła) na podstawie listy faktów.\n\n" +
                                        "Zasady:\n" +
                                        "1. TYTUŁ: Stwórz faktyczny, neutralny nagłówek w języku polskim.\n" +
                                        "2. PODSUMOWANIE: Stwórz 3-4 konkretne akapity (łącznie 400-800 znaków).\n" +
                                        "3. KATEGORIA: Przypisz jedną kategorię (np. BIZNES, POLITYKA, TECH, ŚWIAT, PL).\n" +
                                        "4. FORMAT: Zwróć WYŁĄCZNIE czysty, poprawny obiekt JSON: {\"title\": \"...\", \"essence\": \"...\", \"category\": \"...\"}.\n" +
                                        "Ważne: Wewnątrz stringów używaj wyłącznie apostrofów (') zamiast cudzysłowów (\") lub upewnij się, że każdy cudzysłów jest poprawnie eskapowany (\").";

            try
            {
                if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "PLACEHOLDER_GOOGLE_KEY")
                {
                    _logger.LogWarning("Brak klucza API dla Google AI Studio lub klucz jest domyślny. Przełączanie na OpenRouter.");
                    return await _openRouterClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
                }

                _logger.LogInformation("Krok 2: Generowanie podsumowania za pomocą Google AI Studio (Model: {Model})", _modelName);

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
                            parts = new[] { new { text = factsJson } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.3,
                        responseMimeType = "application/json"
                    }
                };

                var response = await _httpClient.PostAsJsonAsync(requestUri, payloadObject, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Google AI Studio: Przekroczono limit zapytań (429 Too Many Requests). Przełączanie na OpenRouter.");
                    return await _openRouterClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Błąd Google AI Studio API ({StatusCode}). Szczegóły: {Error}. Przełączanie na OpenRouter.", response.StatusCode, error);
                    return await _openRouterClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
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

                _logger.LogWarning("Google AI Studio zwróciło pustą odpowiedź. Przełączanie na OpenRouter.");
                return await _openRouterClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił nieoczekiwany błąd w GoogleAiStudioClient. Przełączanie na OpenRouter.");
                return await _openRouterClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
            }
        }
    }
}
