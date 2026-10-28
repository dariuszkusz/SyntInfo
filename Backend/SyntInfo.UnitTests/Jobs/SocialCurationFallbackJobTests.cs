using Quartz;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Interfaces;
using SyntInfo.Infrastructure.BackgroundJobs;
using Wolverine;

namespace SyntInfo.UnitTests.Jobs;

public class SocialCurationFallbackJobTests
{
    private readonly ILogger<SocialCurationFallbackJob> _logger = Substitute.For<ILogger<SocialCurationFallbackJob>>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _articleRepo = Substitute.For<INewsArticleRepository>();
    private readonly ITelegramNotificationService _telegramService = Substitute.For<ITelegramNotificationService>();
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();

    public SocialCurationFallbackJobTests()
    {
        _uow.NewsArticles.Returns(_articleRepo);
        _context.CancellationToken.Returns(CancellationToken.None);
    }

    [Fact]
    public async Task Execute_WhenDigestAlreadyPublishedToday_DoesNotPublishFallback()
    {
        // Arrange: Wydanie dzisiejsze zostało już opublikowane
        _articleRepo.HasDigestBeenPublishedSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var job = new SocialCurationFallbackJob(_logger, _bus, _uow, _telegramService);

        // Act
        await job.Execute(_context);

        // Assert: Brak wywołania komendy publikacji ani notyfikacji o fallbacku
        await _bus.DidNotReceive().PublishAsync(Arg.Any<PublishDigestIssueCommand>());
        await _telegramService.DidNotReceive().SendSimpleNotificationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenDigestNotYetPublishedToday_TriggersFallbackAndNotifiesTelegram()
    {
        // Arrange: Brak publikacji dzisiaj (redaktor nie zareagował do 9:00)
        _articleRepo.HasDigestBeenPublishedSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var job = new SocialCurationFallbackJob(_logger, _bus, _uow, _telegramService);

        // Act
        await job.Execute(_context);

        // Assert: Wysłano ostrzeżenie o trybie automatycznym oraz wyzwolono PublishDigestIssueCommand
        await _telegramService.Received(1).SendSimpleNotificationAsync(
            Arg.Is<string>(s => s.Contains("Godzina 09:00 — Tryb automatyczny")),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(Arg.Any<PublishDigestIssueCommand>());
    }
}
