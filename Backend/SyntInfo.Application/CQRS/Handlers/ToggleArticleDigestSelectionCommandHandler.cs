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
        ITelegramCurationStateService telegramState,
        IOptions<CurationOptions> curationOptions,
        ILogger<ToggleArticleDigestSelectionCommand> logger,
        CancellationToken cancellationToken)
    {
        var article = await uow.NewsArticles.GetByIdAsync(command.ArticleId, cancellationToken);

        if (article == null)
        {
            logger.LogWarning("Nie znaleziono artykułu {ArticleId} do przełączenia selekcji.", command.ArticleId);
            return;
        }

        var opts = curationOptions.Value;
        var regionName = article.Region switch
        {
            SourceRegion.Poland => "Polska",
            SourceRegion.World => "Świat",
            SourceRegion.Positive => "Pozytywne",
            _ => article.Region.ToString()
        };

        var maxLimit = opts.GetMaxSelectionLimit(article.Region);

        // 1. Sprawdzenie limitu kategorii przy próbie zaznaczenia
        if (!article.IsSelectedForDigest)
        {
            var currentCountInRegion = await uow.NewsArticles.Query()
                .CountAsync(a => a.Region == article.Region && a.IsSelectedForDigest && a.DigestPublishedAt == null, cancellationToken);

            if (currentCountInRegion >= maxLimit)
            {
                logger.LogInformation("Odrzucono zaznaczenie artykułu {ArticleId}: osiągnięto limit {Limit} dla {Region}",
                    article.Id, maxLimit, regionName);

                if (!string.IsNullOrWhiteSpace(command.CallbackQueryId))
                {
                    await telegramService.AnswerCallbackQueryAsync(
                        command.CallbackQueryId,
                        $"⚠️ Osiągnięto limit (maks. {maxLimit} dla: {regionName})! Odznacz inny artykuł, aby wybrać ten.",
                        showAlert: true,
                        cancellationToken: cancellationToken);
                }
                return;
            }
        }

        // 2. Przełączenie stanu w bazie
        article.IsSelectedForDigest = !article.IsSelectedForDigest;
        uow.NewsArticles.Update(article);
        await uow.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Przełączono status artykułu {ArticleId} '{Title}' w wydaniu na: {Selected}",
            article.Id, article.Title, article.IsSelectedForDigest);

        if (!string.IsNullOrWhiteSpace(command.CallbackQueryId))
        {
            var toast = article.IsSelectedForDigest ? $"✅ Wybrano ({regionName})" : $"➖ Usunięto ({regionName})";
            await telegramService.AnswerCallbackQueryAsync(command.CallbackQueryId, toast, showAlert: false, cancellationToken);
        }

        // 3. Aktualizacja klawiatury w zbiorczej wiadomości dla danego regionu
        var proposalLimit = opts.GetProposalLimit(article.Region);

        var regionArticles = await uow.NewsArticles.GetArticlesForCurationByRegionAsync(article.Region, proposalLimit, cancellationToken);
        var targetMessageId = command.TelegramMessageId ?? telegramState.GetCategoryMessageId(article.Region);

        if (targetMessageId.HasValue && regionArticles.Any())
        {
            await telegramService.UpdateThematicBatchMarkupAsync(targetMessageId.Value, article.Region, regionArticles, cancellationToken);
        }

        // 4. Aktualizacja Panelu Kontrolnego ze stanem zaznaczeń
        var polandCount = await uow.NewsArticles.Query().CountAsync(a => a.Region == SourceRegion.Poland && a.IsSelectedForDigest && a.DigestPublishedAt == null, cancellationToken);
        var worldCount = await uow.NewsArticles.Query().CountAsync(a => a.Region == SourceRegion.World && a.IsSelectedForDigest && a.DigestPublishedAt == null, cancellationToken);
        var positiveCount = await uow.NewsArticles.Query().CountAsync(a => a.Region == SourceRegion.Positive && a.IsSelectedForDigest && a.DigestPublishedAt == null, cancellationToken);

        if (telegramState.ControlPanelMessageId.HasValue)
        {
            await telegramService.UpdateDigestControlPanelAsync(telegramState.ControlPanelMessageId.Value, polandCount, worldCount, positiveCount, cancellationToken);
        }
        else if (command.TelegramMessageId.HasValue && targetMessageId != command.TelegramMessageId)
        {
            await telegramService.UpdateCurationMessageAsync(command.TelegramMessageId.Value, article, article.IsSelectedForDigest, cancellationToken);
        }
    }
}
