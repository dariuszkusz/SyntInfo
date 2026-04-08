using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class CleanupOldArticlesCommandHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly ILogger<CleanupOldArticlesCommandHandler> _logger;

        public CleanupOldArticlesCommandHandler(IUnitOfWork uow, ILogger<CleanupOldArticlesCommandHandler> logger)
        {
            _uow = uow;
            _logger = logger;
        }

        public async Task Handle(CleanupOldArticlesCommand command, CancellationToken cancellationToken)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-command.DaysToKeep);
            
            _logger.LogInformation("Rozpoczynanie oczyszczania bazy danych (newsy starsze niż {Date})", cutoffDate);

            var oldArticles = await _uow.Repository<NewsArticle>().Query()
                .Where(a => a.PublishedAt < cutoffDate)
                .ToListAsync(cancellationToken);

            if (oldArticles.Any())
            {
                foreach (var article in oldArticles)
                {
                    _uow.Repository<NewsArticle>().Delete(article);
                }
                
                await _uow.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Usunięto {Count} starych artykułów.", oldArticles.Count);
            }
            else
            {
                _logger.LogInformation("Brak starych artykułów do usunięcia.");
            }
        }
    }
}
