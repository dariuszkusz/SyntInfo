using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

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
        ILogger<SendCurationBatchCommand> logger,
        CancellationToken cancellationToken)
    {
        var count = command.Count > 0 ? command.Count : 5;

        var articles = await uow.Repository<NewsArticle>().Query()
            .Where(a => a.IsActive && a.DigestPublishedAt == null)
            .OrderByDescending(a => a.PublishedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

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
