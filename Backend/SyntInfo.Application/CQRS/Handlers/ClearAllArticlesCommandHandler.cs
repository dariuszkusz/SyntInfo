using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class ClearAllArticlesCommandHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly ILogger<ClearAllArticlesCommandHandler> _logger;

        public ClearAllArticlesCommandHandler(IUnitOfWork uow, ILogger<ClearAllArticlesCommandHandler> logger)
        {
            _uow = uow;
            _logger = logger;
        }

        public async Task Handle(ClearAllArticlesCommand command, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Rozpoczęto czyszczenie bazy danych z wiadomości.");
            await _uow.Repository<NewsArticle>().Query().ExecuteDeleteAsync(cancellationToken);

            var sources = await _uow.Repository<NewsSource>().Query().ToListAsync(cancellationToken);
            foreach (var source in sources)
            {
                source.LastFetchedAt = DateTime.MinValue;
                _uow.Repository<NewsSource>().Update(source);
            }

            await _uow.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Baza danych została wyczyszczona.");
        }
    }
}
