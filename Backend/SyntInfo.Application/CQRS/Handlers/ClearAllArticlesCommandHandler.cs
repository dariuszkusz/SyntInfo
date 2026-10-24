using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Commands;

namespace SyntInfo.Application.CQRS.Handlers;

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

        var sources = await _uow.NewsSources.GetAllSourcesAsync(cancellationToken);
        foreach (var source in sources)
        {
            source.LastFetchedAt = DateTime.MinValue;
            _uow.NewsSources.Update(source);
        }

        await _uow.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Baza danych została wyczyszczona.");
    }
}
