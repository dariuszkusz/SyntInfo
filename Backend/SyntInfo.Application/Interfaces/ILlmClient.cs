using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SyntInfo.Application.Interfaces
{
    public interface ILlmClient
    {
        Task<string> GenerateSummaryAsync(string text, CancellationToken cancellationToken = default);
        Task<float[]> GenerateEmbeddingsAsync(string text, CancellationToken cancellationToken = default);
    }
}
