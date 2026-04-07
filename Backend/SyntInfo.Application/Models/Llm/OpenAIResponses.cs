using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SyntInfo.Application.Models.Llm
{
    public class OpenAIChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAIChoice> Choices { get; set; } = new();
    }

    public class OpenAIChoice
    {
        [JsonPropertyName("message")]
        public OpenAIMessage Message { get; set; } = new();
    }

    public class OpenAIMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    public class OpenAIEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAIEmbeddingData> Data { get; set; } = new();
    }

    public class OpenAIEmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = System.Array.Empty<float>();
    }
}
