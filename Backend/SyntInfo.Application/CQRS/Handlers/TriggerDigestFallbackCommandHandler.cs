using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Application.CQRS.Handlers;

public static class TriggerDigestFallbackCommandHandler
{
    public static async Task Handle(
        TriggerDigestFallbackCommand _,
        IUnitOfWork uow,
        IMessageBus bus,
        ITelegramNotificationService telegramService,
        ILogger<TriggerDigestFallbackCommand> logger,
        CancellationToken cancellationToken)
    {
        var todayStartUtc = DateTime.UtcNow.Date;
        var hasPublishedToday = await uow.NewsArticles.HasDigestBeenPublishedSinceAsync(todayStartUtc, cancellationToken);

        if (hasPublishedToday)
        {
            logger.LogInformation("Wydanie na dzisiaj zostało już opublikowane przez redaktora przed 09:00. Pomijam automatyczny fallback.");
            return;
        }

        logger.LogWarning("Brak reakcji redaktora do godziny 09:00. Uruchamianie automatycznej publikacji w trybie fallback.");
        await telegramService.SendSimpleNotificationAsync(
            "⏰ <b>Godzina 09:00 — Tryb automatyczny (Fallback)</b>\n" +
            "Nie odnotowano zatwierdzenia wydania przez redaktora do godziny 09:00. Następuje automatyczna synteza i publikacja artykułów tak jak dotychczas.",
            cancellationToken);

        await bus.PublishAsync(new PublishDigestIssueCommand());
    }
}

