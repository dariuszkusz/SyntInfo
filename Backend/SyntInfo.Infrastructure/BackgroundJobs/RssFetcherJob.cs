using Microsoft.Extensions.Logging;
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
        _logger.LogInformation("Wyzwalanie procesów tła (RSS + Cleanup) {Time}", DateTime.UtcNow);
        
        // 1. Pobieranie nowych newsów
        await _bus.PublishAsync(new TriggerRssFetchCommand());

        // 2. Usuwanie starych newsów (starszych niż 7 dni)
        await _bus.PublishAsync(new CleanupOldArticlesCommand(DaysToKeep: 7));
    }
}
