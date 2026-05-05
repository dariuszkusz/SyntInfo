using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SyntInfo.Application.Interfaces
{
    public interface ILlmClient
    {
        Task<string> GenerateSummaryAsync(string text, CancellationToken cancellationToken = default);
        Task<float[]> GenerateEmbeddingsAsync(string text, CancellationToken cancellationToken = default);
        Task<List<int>> SelectTopArticlesIndexesAsync(string articlesListJson, int expectedCount, CancellationToken cancellationToken = default);
        Task<string> GenerateEnrichedSummaryAsync(string basicContent, string searchContent, CancellationToken cancellationToken = default);
    }
}
