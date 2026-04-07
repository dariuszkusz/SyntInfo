using Microsoft.Extensions.Logging;
using Quartz;

namespace SyntInfo.Infrastructure.BackgroundJobs;

public class RssFetcherJob : IJob
{
    private readonly ILogger<RssFetcherJob> _logger;

    public RssFetcherJob(ILogger<RssFetcherJob> logger)
    {
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Rozpoczęto pobieranie i klastrowanie feedów RSS {Time}", DateTime.UtcNow);
        
        // Tutaj znajdzie sie wywołanie np. ProcessRssFeedsCommand (CQRS)
        // do wysłania requestu przez MediatR/własny handler.
        
        await Task.CompletedTask;
    }
}
