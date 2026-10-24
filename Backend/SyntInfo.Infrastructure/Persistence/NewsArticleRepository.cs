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
}
