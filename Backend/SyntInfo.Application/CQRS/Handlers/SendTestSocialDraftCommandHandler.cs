using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.Application.CQRS.Handlers;

/// <summary>
/// Handler CQRS tworzący testowy szkic posta i wysyłający go do administratora na Telegramie.
/// </summary>
public static class SendTestSocialDraftCommandHandler
{
    public static async Task<SendTestDraftResult> Handle(
        SendTestSocialDraftCommand command,
        IUnitOfWork uow,
        ITelegramNotificationService telegramService,
        ILogger<SendTestSocialDraftCommand> logger,
        CancellationToken cancellationToken)
    {
        var platform = command.Platform;

        // 1. Sprawdź lub utwórz przykładowy artykuł
        var article = await uow.Repository<NewsArticle>().Query()
            .FirstOrDefaultAsync(cancellationToken);

        if (article == null)
        {
            article = new NewsArticle
            {
                Title = "Przełom w fuzji jądrowej - SyntInfo AI",
                OriginalTitle = "Breakthrough in Nuclear Fusion",
                SummaryText = "Europejscy inżynierowie osiągnęli stabilną plazmę fuzyjną przez ponad 12 minut. Jest to rekordowy wynik przybliżający komercyjne reaktory nowej generacji.",
                PublishedAt = DateTime.UtcNow,
                SourceUrls = new List<string> { "https://syntinfo.local/news/fusion" },
                IsActive = true
            };
            await uow.Repository<NewsArticle>().AddAsync(article, cancellationToken);
            await uow.SaveChangesAsync(cancellationToken);
        }

        // 2. Utwórz szkic posta z wyraźnym oznaczeniem testowym
        var draft = new SocialPostDraft
        {
            NewsArticleId = article.Id,
            PlatformType = platform,
            Content = $"[TEST - NIE KLIKAĆ]\n⚡ {article.Title}\n\n{article.SummaryText}\n\n🔗 Czytaj więcej: {article.SourceUrls.FirstOrDefault()}\n#technologia #nauka #syntinfo #test",
            Status = SocialPostStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        await uow.Repository<SocialPostDraft>().AddAsync(draft, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        // 3. Wyślij do administratora przez TelegramNotificationService
        try
        {
            var msgId = await telegramService.SendDraftToAdminAsync(draft, article, cancellationToken);
            if (msgId > 0)
            {
                draft.TelegramMessageId = msgId;
                uow.Repository<SocialPostDraft>().Update(draft);
                await uow.SaveChangesAsync(cancellationToken);
            }

            return new SendTestDraftResult(
                Success: true,
                DraftId: draft.Id,
                Platform: platform.ToString(),
                TelegramMessageId: msgId,
                Message: $"Wysłano testowy szkic posta ({platform}) na Telegram!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd wysyłania testowego szkicu do Telegrama");
            return new SendTestDraftResult(
                Success: false,
                DraftId: draft.Id,
                Platform: platform.ToString(),
                TelegramMessageId: 0,
                Message: "Błąd podczas wysyłania testowego szkicu.",
                Error: ex.Message);
        }
    }
}
