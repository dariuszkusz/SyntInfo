using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.Infrastructure.Persistence
{
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

        public UnitOfWork(AppDbContext db) => _db = db;

        public IRepository<T> Repository<T>() where T : class
        {
            return (IRepository<T>)_repositories.GetOrAdd(typeof(T), _ => new Repository<T>(_db));
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => await _db.SaveChangesAsync(cancellationToken);

        public void Dispose()
        {
            _db.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
