using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Llm;

namespace SyntInfo.Infrastructure.Services
{
    public class LocalLlmClient : ILlmClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelName;

        public LocalLlmClient(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            // Domyślny model to ai/llama3.2:latest, jak podał użytkownik
            _modelName = configuration["Llm:ModelName"] ?? "ai/llama3.2:latest";
        }

        public async Task<string> GenerateSummaryAsync(string text, CancellationToken cancellationToken = default)
        {
            var request = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = "Jesteś architektem informacji. Twoim zadaniem jest przekształcenie artykułów informacyjnych w czystą 'Infopigułę'.\n\nZasady:\n1. TYTUŁ: Stwórz faktyczny, neutralny nagłówek pozbawiony clickbaitu w języku polskim.\n2. ESENCJA: Wyciągnij kluczowe fakty w formie 3 konkretnych akapitów. Łączna długość tekstu esencji MUSI mieścić się w przedziale 300-700 znaków. Każdy fakt powinien być treściwy i precyzyjny.\n3. KATEGORIA: Przypisz jedną kategorię (np. BIZNES, POLITYKA, TECH, ŚWIAT, PL).\n4. FORMAT: Zwróć WYŁĄCZNIE obiekt JSON: {\"title\": \"...\", \"essence\": \"...\", \"category\": \"...\"}." },
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
                    input = text
                };

                var response = await _httpClient.PostAsJsonAsync("v1/embeddings", request, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    System.Console.WriteLine($"[LLM Error] Embeddings failed: {response.StatusCode} - {errorBody}");
                    return System.Array.Empty<float>();
                }

                var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(cancellationToken: cancellationToken);
                if (result?.Data != null && result.Data.Count > 0)
                {
                    return result.Data[0].Embedding;
                }
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[LLM Exception] Embeddings: {ex.Message}");
            }

            return System.Array.Empty<float>();
        }
    }
}
