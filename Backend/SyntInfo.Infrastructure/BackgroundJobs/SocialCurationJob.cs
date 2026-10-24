using Microsoft.Extensions.Configuration;
using Quartz;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Infrastructure.BackgroundJobs;

[DisallowConcurrentExecution]
public class SocialCurationJob : IJob
{
    private readonly ILogger<SocialCurationJob> _logger;
    private readonly IMessageBus _bus;
    private readonly IConfiguration _configuration;

    public SocialCurationJob(
        ILogger<SocialCurationJob> logger,
        IMessageBus bus,
        IConfiguration configuration)
    {
        _logger = logger;
        _bus = bus;
        _configuration = configuration;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Wyzwalanie procesu przygotowania postów na platformy społecznościowe {Time}", DateTime.UtcNow);

        context.CancellationToken.ThrowIfCancellationRequested();

        var count = _configuration.GetValue("SocialPlatforms:MaxArticlesPerRun", 5);

        var result = await _bus.InvokeAsync<SendCurationBatchResult>(
            new SendCurationBatchCommand(count),
            context.CancellationToken);

        _logger.LogInformation(
            "Zakończono proces SendCurationBatch. Sukces: {Success}, Wysłano artykułów: {Count}, Komunikat: {Message}",
            result.Success,
            result.SentCount,
            result.Message);
    }
}
