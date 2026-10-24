using System.Collections.Concurrent;

namespace SyntInfo.Infrastructure.Persistence;

public class Repository<T> : IRepository<T> where T : class
{
    private readonly AppDbContext _db;
    public Repository(AppDbContext db) => _db = db;

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => await _db.Set<T>().FindAsync(new object[] { id }, cancellationToken);
    public IQueryable<T> Query() => _db.Set<T>().AsQueryable();
    public async Task AddAsync(T entity, CancellationToken cancellationToken = default) => await _db.Set<T>().AddAsync(entity, cancellationToken);
    public void Update(T entity) => _db.Set<T>().Update(entity);
    public void Delete(T entity) => _db.Set<T>().Remove(entity);
}

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    private readonly ConcurrentDictionary<Type, object> _repositories = new();
    private INewsSourceRepository? _newsSources;
    private INewsArticleRepository? _newsArticles;

    public UnitOfWork(AppDbContext db) => _db = db;

    public INewsSourceRepository NewsSources => _newsSources ??= new NewsSourceRepository(_db);
    public INewsArticleRepository NewsArticles => _newsArticles ??= new NewsArticleRepository(_db);

    public IRepository<T> Repository<T>() where T : class
    {
        if (typeof(T) == typeof(SyntInfo.Domain.Entities.NewsSource))
        {
            return (IRepository<T>)NewsSources;
        }

        if (typeof(T) == typeof(SyntInfo.Domain.Entities.NewsArticle))
        {
            return (IRepository<T>)NewsArticles;
        }

        return (IRepository<T>)_repositories.GetOrAdd(typeof(T), _ => new Repository<T>(_db));
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => await _db.SaveChangesAsync(cancellationToken);

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
