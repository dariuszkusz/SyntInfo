using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Commands;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

public static class ToggleArticleDigestSelectionCommandHandler
{
    [MessageTimeout(60)]
    public static async Task Handle(
        ToggleArticleDigestSelectionCommand command,
        IUnitOfWork uow,
        ITelegramNotificationService telegramService,
        ILogger<ToggleArticleDigestSelectionCommand> logger,
        CancellationToken cancellationToken)
    {
        var article = await uow.Repository<NewsArticle>().Query()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, cancellationToken);

        if (article == null)
        {
            logger.LogWarning("Nie znaleziono artykułu {ArticleId} do przełączenia selekcji.", command.ArticleId);
            return;
        }

        article.IsSelectedForDigest = !article.IsSelectedForDigest;
        uow.Repository<NewsArticle>().Update(article);
        await uow.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Przełączono status artykułu {ArticleId} '{Title}' w wydaniu na: {Selected}",
            article.Id, article.Title, article.IsSelectedForDigest);

        if (command.TelegramMessageId.HasValue)
        {
            await telegramService.UpdateCurationMessageAsync(
                command.TelegramMessageId.Value,
                article,
                article.IsSelectedForDigest,
                cancellationToken);
        }
    }
}
