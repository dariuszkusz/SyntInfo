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
                    new { role = "system", content = "You are an expert journalist. Summarize the following news text concisely." },
                    new { role = "user", content = text }
                },
                temperature = 0.3
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
            var request = new
            {
                model = _modelName,
                input = text
            };

            var response = await _httpClient.PostAsJsonAsync("v1/embeddings", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(cancellationToken: cancellationToken);
            if (result?.Data != null && result.Data.Count > 0)
            {
                return result.Data[0].Embedding;
            }

            return System.Array.Empty<float>();
        }
    }
}
