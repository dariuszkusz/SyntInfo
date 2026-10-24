namespace SyntInfo.Infrastructure.Persistence;

public class NewsSourceRepository : Repository<NewsSource>, INewsSourceRepository
{
    private readonly AppDbContext _db;

    public NewsSourceRepository(AppDbContext db) : base(db)
    {
        _db = db;
    }

    public async Task<List<NewsSource>> GetActiveSourcesAsync(CancellationToken cancellationToken = default)
    {
        return await _db.NewsSources
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<NewsSource>> GetAllSourcesAsync(CancellationToken cancellationToken = default)
    {
        return await _db.NewsSources
            .ToListAsync(cancellationToken);
    }
}
