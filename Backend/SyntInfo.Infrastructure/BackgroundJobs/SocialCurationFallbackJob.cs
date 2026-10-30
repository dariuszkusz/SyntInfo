using Quartz;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Infrastructure.BackgroundJobs;

/// <summary>
/// Zadanie w tle Quartz uruchamiane o 9:00 rano jako automatyczny fallback.
/// Deleguje logikę decyzyjną do handlera CQRS (TriggerDigestFallbackCommandHandler).
/// </summary>
[DisallowConcurrentExecution]
public class SocialCurationFallbackJob : IJob
{
    private readonly ILogger<SocialCurationFallbackJob> _logger;
    private readonly IMessageBus _bus;

    public SocialCurationFallbackJob(
        ILogger<SocialCurationFallbackJob> logger,
        IMessageBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Wyzwalanie sprawdzania warunku fallback publikacji o godzinie 09:00 {Time}", DateTime.UtcNow);
        context.CancellationToken.ThrowIfCancellationRequested();

        await _bus.InvokeAsync(new TriggerDigestFallbackCommand(), context.CancellationToken);
    }
}
