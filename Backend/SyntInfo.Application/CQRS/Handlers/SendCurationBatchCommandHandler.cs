using SyntInfo.Application.CQRS.Commands;

namespace SyntInfo.Application.CQRS.Handlers;

/// <summary>
/// Handler CQRS wysyłający partię artykułów do selekcji na Telegramie.
/// </summary>
public static class SendCurationBatchCommandHandler
{
    public static async Task<SendCurationBatchResult> Handle(
        SendCurationBatchCommand command,
        IUnitOfWork uow,
        ITelegramNotificationService telegramService,
        IOptions<CurationOptions> curationOptions,
        CancellationToken cancellationToken)
    {
        var count = command.Count is > 0 ? command.Count.Value : curationOptions.Value.MaxArticlesPerRun;

        var articles = await uow.NewsArticles.GetArticlesForCurationAsync(count, cancellationToken);

        if (!articles.Any())
        {
            return new SendCurationBatchResult(
                Success: true,
                SentCount: 0,
                Message: "Brak nowych artykułów oczekujących na selekcję do wydania.");
        }

        var selectedCount = 0;
        foreach (var article in articles)
        {
            if (article.IsSelectedForDigest) selectedCount++;
            await telegramService.SendArticleForCurationAsync(article, article.IsSelectedForDigest, cancellationToken);
            // Drobny delay, aby zapobiec ograniczeniom Telegram API
            await Task.Delay(300, cancellationToken);
        }

        await telegramService.SendDigestControlPanelAsync(selectedCount, cancellationToken);

        return new SendCurationBatchResult(
            Success: true,
            SentCount: articles.Count,
            Message: $"Wysłano {articles.Count} artykułów do selekcji na Telegram wraz z panelem publikacji wydania.");
    }
}
