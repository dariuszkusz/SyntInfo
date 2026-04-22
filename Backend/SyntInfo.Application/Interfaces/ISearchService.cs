using System.Threading;
using System.Threading.Tasks;

namespace SyntInfo.Application.Interfaces
{
    public interface ISearchService
    {
        Task<string> SearchDetailedInfoAsync(string query, CancellationToken cancellationToken = default);
    }
}
