using Quartz;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Infrastructure.BackgroundJobs;
using Wolverine;

namespace SyntInfo.UnitTests.Jobs;

public class RssFetcherJobTests
{
    private readonly ILogger<RssFetcherJob> _logger = Substitute.For<ILogger<RssFetcherJob>>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();

    [Fact]
    public async Task Execute_WhenNotCancelled_PublishesTriggerRssFetchAndCleanupCommands()
    {
        // Arrange
        _context.CancellationToken.Returns(CancellationToken.None);
        var job = new RssFetcherJob(_logger, _bus);

        // Act
        await job.Execute(_context);

        // Assert
        await _bus.Received(1).PublishAsync(Arg.Any<TriggerRssFetchCommand>());
        await _bus.Received(1).PublishAsync(Arg.Is<CleanupOldArticlesCommand>(c => c.DaysToKeep == 7));
    }

    [Fact]
    public async Task Execute_WhenCancelledInitially_ThrowsOperationCanceledExceptionAndDoesNotPublish()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _context.CancellationToken.Returns(cts.Token);

        var job = new RssFetcherJob(_logger, _bus);

        // Act
        var act = async () => await job.Execute(_context);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _bus.DidNotReceive().PublishAsync(Arg.Any<TriggerRssFetchCommand>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<CleanupOldArticlesCommand>());
    }

    [Fact]
    public async Task Execute_WhenCancelledAfterFirstPublish_ThrowsOperationCanceledExceptionAndDoesNotPublishCleanup()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _context.CancellationToken.Returns(cts.Token);

        _bus.PublishAsync(Arg.Any<TriggerRssFetchCommand>())
            .Returns(_ =>
            {
                cts.Cancel();
                return ValueTask.CompletedTask;
            });

        var job = new RssFetcherJob(_logger, _bus);

        // Act
        var act = async () => await job.Execute(_context);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _bus.Received(1).PublishAsync(Arg.Any<TriggerRssFetchCommand>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<CleanupOldArticlesCommand>());
    }
}
