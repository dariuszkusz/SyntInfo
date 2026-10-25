using Quartz;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Infrastructure.BackgroundJobs;

[DisallowConcurrentExecution]
public class SocialCurationJob : IJob
{
    private readonly ILogger<SocialCurationJob> _logger;
    private readonly IMessageBus _bus;

    public SocialCurationJob(
        ILogger<SocialCurationJob> logger,
        IMessageBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Wyzwalanie procesu przygotowania postów na platformy społecznościowe {Time}", DateTime.UtcNow);

        context.CancellationToken.ThrowIfCancellationRequested();

        var result = await _bus.InvokeAsync<SendCurationBatchResult>(
            new SendCurationBatchCommand(),
            context.CancellationToken);

        _logger.LogInformation(
            "Zakończono proces SendCurationBatch. Sukces: {Success}, Wysłano artykułów: {Count}, Komunikat: {Message}",
            result.Success,
            result.SentCount,
            result.Message);
    }
}
