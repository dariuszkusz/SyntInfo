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
        return await FilterValidActive(_db.NewsArticles)
            .OrderByDescending(a => a.PublishedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<NewsArticle>> GetArticlesForCurationAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await FilterValidActive(_db.NewsArticles)
            .Where(a => a.DigestPublishedAt == null)
            .OrderByDescending(a => a.PublishedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<NewsArticle>> GetArticlesForCurationByRegionAsync(SourceRegion region, int limit, CancellationToken cancellationToken = default)
    {
        return await FilterValidActive(_db.NewsArticles)
            .Where(a => a.Region == region && a.DigestPublishedAt == null)
            .OrderByDescending(a => a.PublishedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasDigestBeenPublishedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        return await _db.NewsArticles
            .AnyAsync(a => a.DigestPublishedAt != null && a.DigestPublishedAt >= sinceUtc, cancellationToken);
    }

    public async Task<List<NewsArticle>> GetRecentArticlesAsync(SourceRegion? region, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddHours(-24);
        var query = FilterValidActive(_db.NewsArticles.Include(a => a.Category))
            .Where(a => a.PublishedAt >= cutoffDate);

        if (region.HasValue)
        {
            query = query.Where(a => a.Region == region.Value);
        }

        return await query
            .OrderByDescending(a => a.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await _db.NewsArticles.ExecuteDeleteAsync(cancellationToken);
    }

    private static IQueryable<NewsArticle> FilterValidActive(IQueryable<NewsArticle> query)
    {
        return query.Where(a => a.IsActive
                             && a.SummaryText.Length >= 40
                             && !a.Title.Contains("Brak faktów")
                             && !a.Title.Contains("Brak danych")
                             && !a.SummaryText.Contains("User Safety")
                             && !a.SummaryText.Contains("safety filter")
                             && !a.SummaryText.Contains("Brak faktów")
                             && !a.SummaryText.Contains("Nie podano danych"));
    }
}
