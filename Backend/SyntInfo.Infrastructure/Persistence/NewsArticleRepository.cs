namespace SyntInfo.Infrastructure.Persistence;

public class NewsArticleRepository : Repository<NewsArticle>, INewsArticleRepository
{
    private readonly AppDbContext _db;

    public NewsArticleRepository(AppDbContext db) : base(db)
    {
        _db = db;
    }

    public async Task<bool> ExistsByUrlOrTitleAsync(string url, string title, CancellationToken cancellationToken = default)
    {
        return await _db.NewsArticles
            .AnyAsync(a => a.SourceUrls.Contains(url) || a.Title == title, cancellationToken);
    }

    public async Task<List<NewsArticle>> GetSelectedForDigestAsync(CancellationToken cancellationToken = default)
    {
        return await _db.NewsArticles
            .Where(a => a.IsSelectedForDigest && a.DigestPublishedAt == null)
            .OrderByDescending(a => a.PublishedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<NewsArticle>> GetActiveArticlesAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await _db.NewsArticles
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.PublishedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<NewsArticle>> GetArticlesForCurationAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await _db.NewsArticles
            .Where(a => a.IsActive && a.DigestPublishedAt == null)
            .OrderByDescending(a => a.PublishedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}

