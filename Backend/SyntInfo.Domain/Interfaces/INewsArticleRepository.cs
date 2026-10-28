using SyntInfo.Domain.Entities;

namespace SyntInfo.Domain.Interfaces;

public interface INewsArticleRepository : IRepository<NewsArticle>
{
    Task<bool> ExistsByUrlOrTitleAsync(string url, string title, CancellationToken cancellationToken = default);
    Task<List<NewsArticle>> GetSelectedForDigestAsync(CancellationToken cancellationToken = default);
    Task<List<NewsArticle>> GetActiveArticlesAsync(int limit, CancellationToken cancellationToken = default);
    Task<List<NewsArticle>> GetArticlesForCurationAsync(int limit, CancellationToken cancellationToken = default);
    Task<List<NewsArticle>> GetArticlesForCurationByRegionAsync(SourceRegion region, int limit, CancellationToken cancellationToken = default);
    Task<bool> HasDigestBeenPublishedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
    Task<List<NewsArticle>> GetRecentArticlesAsync(SourceRegion? region, int page, int pageSize, CancellationToken cancellationToken = default);
}
