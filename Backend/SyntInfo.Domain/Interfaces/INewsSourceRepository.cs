using SyntInfo.Domain.Entities;

namespace SyntInfo.Domain.Interfaces;

public interface INewsSourceRepository : IRepository<NewsSource>
{
    Task<List<NewsSource>> GetActiveSourcesAsync(CancellationToken cancellationToken = default);
    Task<List<NewsSource>> GetAllSourcesAsync(CancellationToken cancellationToken = default);
}
