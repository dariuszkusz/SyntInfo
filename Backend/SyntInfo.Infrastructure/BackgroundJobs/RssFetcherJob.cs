using Microsoft.Extensions.Logging;
using Quartz;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Infrastructure.BackgroundJobs;

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
        _logger.LogInformation("Wyzwalanie procesu pobierania RSS przez Wolverine {Time}", DateTime.UtcNow);
        
        // Wysyłamy komendę do Wolverine
        await _bus.PublishAsync(new TriggerRssFetchCommand());
    }
}
