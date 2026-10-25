using System.Net.Http.Json;
using SyntInfo.Application.Models.Llm;
using SyntInfo.Application.Prompts;

namespace SyntInfo.Infrastructure.Services;

public class LocalLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private readonly bool _useMock;

    public LocalLlmClient(HttpClient httpClient, IOptions<LocalLlmOptions> localLlmOptions)
    {
        _httpClient = httpClient;
        _modelName = !string.IsNullOrWhiteSpace(localLlmOptions.Value.ModelName) ? localLlmOptions.Value.ModelName : "ai/llama3.2:latest";
        _useMock = localLlmOptions.Value.UseMock;
    }
    public async Task<string> GenerateSummaryAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_useMock)
        {
            return "{\"title\": \"[MOCK] Testowy tytuł\", \"essence\": \"Krótka esencja testowa.\", \"category\": \"GENERAL\"}";
        }

        var request = new
        {
            model = _modelName,
            messages = new[]
            {
                new { role = "system", content = AiPrompts.Summary.DirectSummarySystemPrompt },
                new { role = "user", content = text }
            },
            temperature = 0.2, // Niższa temperatura dla większej stabilności JSONa
            response_format = new { type = "json_object" }
        };

        var response = await _httpClient.PostAsJsonAsync("v1/chat/completions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAIChatCompletionResponse>(cancellationToken: cancellationToken);
        if (result?.Choices != null && result.Choices.Count > 0)
        {
            return result.Choices[0].Message.Content;
        }

        return string.Empty;
    }

    public async Task<float[]> GenerateEmbeddingsAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new
            {
                model = _modelName,
                prompt = text
            };

            var response = await _httpClient.PostAsJsonAsync("api/embeddings", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[LLM Error] Embeddings failed: {response.StatusCode} - {errorBody}");
                return Array.Empty<float>();
            }

            using var jsonDoc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (jsonDoc.RootElement.TryGetProperty("embedding", out var embeddingElement))
            {
                return System.Text.Json.JsonSerializer.Deserialize<float[]>(embeddingElement.GetRawText()) ?? System.Array.Empty<float>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM Exception] Embeddings: {ex.Message}");
        }

        return Array.Empty<float>();
    }
    public async Task<List<int>> SelectTopArticlesIndexesAsync(string articlesListJson, int expectedCount, Domain.Entities.SourceRegion region, CancellationToken cancellationToken = default)
    {
        if (_useMock)
        {
            // Zwracamy pierwsze 5 indeksów dla testu
            return new List<int> { 0, 1, 2, 3, 4 };
        }

        var systemPrompt = AiPrompts.ArticleSelection.BuildSystemPrompt(expectedCount, region);

        var request = new
        {
            model = _modelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = articlesListJson }
            },
            temperature = 0.1,
            response_format = new { type = "json_object" }
        };

        var response = await _httpClient.PostAsJsonAsync("v1/chat/completions", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"LLM Request Failed: {response.StatusCode}. Details: {errorBody}");
        }
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);

        OpenAIChatCompletionResponse? result = null;
        try
        {
            result = System.Text.Json.JsonSerializer.Deserialize<OpenAIChatCompletionResponse>(rawJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM Error] Deserialization failed: {ex.Message}. Raw: {rawJson}");
        }

        if (result?.Choices != null && result.Choices.Count > 0)
        {
            var responseContent = result.Choices[0].Message.Content;
            try
            {
                var cleanRes = responseContent.Replace("```json", "").Replace("```", "").Trim();
                using var doc = System.Text.Json.JsonDocument.Parse(cleanRes);
                if (doc.RootElement.TryGetProperty("selectedIndexes", out var arrayEl))
                {
                    var list = new List<int>();
                    foreach (var el in arrayEl.EnumerateArray())
                    {
                        if (el.TryGetInt32(out int val)) list.Add(val);
                    }
                    return list;
                }
                else
                {
                    Console.WriteLine($"[LLM Error] Brak klucza 'selectedIndexes'. Content: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LLM Exception] Błąd parsowania treści: {ex.Message}. Content: {responseContent}");
            }
        }
        else
        {
            Console.WriteLine($"[LLM Error] Choices jest puste lub null. Raw response: {rawJson}");
        }
        return new List<int>();
    }

    public async Task<string> GenerateEnrichedSummaryAsync(string basicContent, string searchContent, CancellationToken cancellationToken = default)
    {
        if (_useMock)
        {
            return "{\"title\": \"[MOCK] To jest testowy tytuł infopiguły\", \"essence\": \"To jest przykładowa esencja wygenerowana przez mocka LLM. Zawiera trzy konkretne fakty na temat Twoich newsów. System działa poprawnie i zapisuje dane.\", \"category\": \"TECH\"}";
        }

        var request = new
        {
            model = _modelName,
            messages = new[]
            {
                new { role = "system", content = "Jesteś architektem informacji. Twoim zadaniem jest przekształcenie artykułów informacyjnych w czystą 'Infopigułę'. Otrzymujesz ZARÓWNO oryginalny opis z RSS, jak i wynik POGŁĘBIONEGO wyszukiwania dla pełnego kontekstu.\n\nZasady:\n1. TYTUŁ: Stwórz faktyczny, neutralny nagłówek pozbawiony clickbaitu w języku polskim.\n2. ESENCJA: Wyciągnij kluczowe fakty z OBU źródeł w formie 3-4 konkretnych akapitów. Łączna długość tekstu esencji: 300-800 znaków.\n3. KATEGORIA: Przypisz jedną kategorię (np. BIZNES, POLITYKA, TECH, ŚWIAT, PL).\n4. FORMAT: Zwróć WYŁĄCZNIE czysty, poprawny obiekt JSON. \n   Struktura: {\"title\": \"string\", \"essence\": \"string\", \"category\": \"string\"}.\n   Ważne: Wewnątrz stringów używaj wyłącznie apostrofów (') zamiast cudzysłowów (\") lub upewnij się, że każdy cudzysłów jest poprawnie eskapowany (\\\"). \n   ABSOLUTNIE ZABRONIONE: konkatenacja (+), komentarze, markdown (```), tekst przed lub po JSON." },
                new { role = "user", content = $"[ORYGINAŁ Z RSS]:\n{basicContent}\n\n[WYNIKI GŁĘBOKIEGO WYSZUKIWANIA]:\n{searchContent}" }
            },
            temperature = 0.2,
            response_format = new { type = "json_object" }
        };

        var response = await _httpClient.PostAsJsonAsync("v1/chat/completions", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"LLM Request Failed: {response.StatusCode}. Details: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<OpenAIChatCompletionResponse>(cancellationToken: cancellationToken);
        if (result?.Choices != null && result.Choices.Count > 0)
        {
            return result.Choices[0].Message.Content;
        }

        return string.Empty;
    }
}
