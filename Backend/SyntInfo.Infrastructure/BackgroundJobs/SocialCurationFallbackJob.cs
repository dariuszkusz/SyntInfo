using Quartz;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Infrastructure.BackgroundJobs;

/// <summary>
/// Zadanie w tle Quartz uruchamiane o 9:00 rano jako automatyczny fallback.
/// Jeśli redaktor nie zatwierdził wydania do godziny 9:00, system publikuje artykuły w trybie automatycznym (dotychczasowym).
/// </summary>
[DisallowConcurrentExecution]
public class SocialCurationFallbackJob : IJob
{
    private readonly ILogger<SocialCurationFallbackJob> _logger;
    private readonly IMessageBus _bus;
    private readonly IUnitOfWork _uow;
    private readonly ITelegramNotificationService _telegramService;

    public SocialCurationFallbackJob(
        ILogger<SocialCurationFallbackJob> logger,
        IMessageBus bus,
        IUnitOfWork uow,
        ITelegramNotificationService telegramService)
    {
        _logger = logger;
        _bus = bus;
        _uow = uow;
        _telegramService = telegramService;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Sprawdzanie warunku fallback publikacji o godzinie 09:00 {Time}", DateTime.UtcNow);

        // Sprawdź czy dzisiejsze wydanie zostało już opublikowane (od początku dzisiejszego dnia UTC)
        var todayStartUtc = DateTime.UtcNow.Date;
        var hasPublishedToday = await _uow.NewsArticles.HasDigestBeenPublishedSinceAsync(todayStartUtc, context.CancellationToken);

        if (hasPublishedToday)
        {
            _logger.LogInformation("Wydanie na dzisiaj zostało już opublikowane przez redaktora przed 09:00. Pomijam automatyczny fallback.");
            return;
        }

        _logger.LogWarning("Brak reakcji redaktora do godziny 09:00. Uruchamianie automatycznej publikacji w trybie fallback.");
        await _telegramService.SendSimpleNotificationAsync(
            "⏰ <b>Godzina 09:00 — Tryb automatyczny (Fallback)</b>\n" +
            "Nie odnotowano zatwierdzenia wydania przez redaktora do godziny 09:00. Następuje automatyczna synteza i publikacja artykułów tak jak dotychczas.",
            context.CancellationToken);

        await _bus.PublishAsync(new PublishDigestIssueCommand());
    }
}
