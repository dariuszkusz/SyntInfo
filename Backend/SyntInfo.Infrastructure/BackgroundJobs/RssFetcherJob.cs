using Quartz;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Infrastructure.BackgroundJobs;

[DisallowConcurrentExecution]
public class RssFetcherJob : IJob
{
    private readonly ILogger<RssFetcherJob> _logger;
    private readonly IMessageBus _bus;

    public RssFetcherJob(ILogger<RssFetcherJob> logger, IMessageBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Wyzwalanie procesów tła (RSS + Cleanup + Kuracja Telegram) {Time}", DateTime.UtcNow);

        context.CancellationToken.ThrowIfCancellationRequested();
        // 1. Pobieranie nowych newsów i pełne opracowanie przez AI (oczekiwanie na zakończenie syntezy)
        await _bus.InvokeAsync(new TriggerRssFetchCommand(), context.CancellationToken);

        context.CancellationToken.ThrowIfCancellationRequested();
        // 2. Usuwanie starych newsów (starszych niż 7 dni)
        await _bus.PublishAsync(new CleanupOldArticlesCommand(DaysToKeep: 7));

        context.CancellationToken.ThrowIfCancellationRequested();
        // 3. Wysłanie powiadomienia do redaktora na Telegram dopiero po pełnym opracowaniu artykułów
        var result = await _bus.InvokeAsync<SendCurationBatchResult>(
            new SendCurationBatchCommand(),
            context.CancellationToken);

        _logger.LogInformation(
            "Zakończono proces SendCurationBatch po opracowaniu artykułów. Sukces: {Success}, Wysłano artykułów: {Count}, Komunikat: {Message}",
            result?.Success ?? false,
            result?.SentCount ?? 0,
            result?.Message);
    }
}
