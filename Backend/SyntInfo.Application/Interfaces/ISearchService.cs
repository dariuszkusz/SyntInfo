using System.Threading;
using System.Threading.Tasks;

namespace SyntInfo.Application.Interfaces
{
    public class SearchResultInfo
    {
        public string Content { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
    }

    public interface ISearchService
    {
        Task<string> SearchDetailedInfoAsync(string query, CancellationToken cancellationToken = default);
        Task<SearchResultInfo> SearchDetailedWithImageAsync(string query, CancellationToken cancellationToken = default);
    }
}
