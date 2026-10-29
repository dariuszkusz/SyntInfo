using Microsoft.Extensions.Options;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class SendCurationBatchCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _articleRepo = Substitute.For<INewsArticleRepository>();
    private readonly ITelegramNotificationService _telegramService = Substitute.For<ITelegramNotificationService>();
    private readonly ITelegramCurationStateService _telegramState = Substitute.For<ITelegramCurationStateService>();
    private readonly IOptions<CurationOptions> _options = Options.Create(new CurationOptions
    {
        ProposalPolandCount = 10,
        ProposalWorldCount = 10,
        ProposalPositiveCount = 10
    });

    public SendCurationBatchCommandHandlerTests()
    {
        _uow.NewsArticles.Returns(_articleRepo);
    }

    [Fact]
    public async Task Handle_WhenArticlesExist_SendsThematicBatchesAndControlPanel()
    {
        // Arrange
        var polandList = new List<NewsArticle> { new() { Id = Guid.NewGuid(), Title = "PL 1", Region = SourceRegion.Poland } };
        var worldList = new List<NewsArticle> { new() { Id = Guid.NewGuid(), Title = "World 1", Region = SourceRegion.World } };
        var positiveList = new List<NewsArticle> { new() { Id = Guid.NewGuid(), Title = "Pos 1", Region = SourceRegion.Positive } };

        _articleRepo.GetArticlesForCurationByRegionAsync(SourceRegion.Poland, 10, Arg.Any<CancellationToken>()).Returns(polandList);
        _articleRepo.GetArticlesForCurationByRegionAsync(SourceRegion.World, 10, Arg.Any<CancellationToken>()).Returns(worldList);
        _articleRepo.GetArticlesForCurationByRegionAsync(SourceRegion.Positive, 10, Arg.Any<CancellationToken>()).Returns(positiveList);

        _telegramService.SendThematicBatchAsync(SourceRegion.Poland, polandList, Arg.Any<CancellationToken>()).Returns(101L);
        _telegramService.SendThematicBatchAsync(SourceRegion.World, worldList, Arg.Any<CancellationToken>()).Returns(102L);
        _telegramService.SendThematicBatchAsync(SourceRegion.Positive, positiveList, Arg.Any<CancellationToken>()).Returns(103L);
        _telegramService.SendDigestControlPanelAsync(0, 0, 0, Arg.Any<CancellationToken>()).Returns(999L);

        var command = new SendCurationBatchCommand();

        // Act
        var result = await SendCurationBatchCommandHandler.Handle(
            command,
            _uow,
            _telegramService,
            _telegramState,
            _options,
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.SentCount);
        _telegramState.Received(1).Clear();
        _telegramState.Received(1).SetCategoryMessageId(SourceRegion.Poland, 101L);
        _telegramState.Received(1).SetCategoryMessageId(SourceRegion.World, 102L);
        _telegramState.Received(1).SetCategoryMessageId(SourceRegion.Positive, 103L);
        _telegramState.Received(1).ControlPanelMessageId = 999L;
    }

    [Fact]
    public async Task Handle_WhenNoArticlesInAnyRegion_ReturnsZeroWithoutSendingBatches()
    {
        // Arrange
        _articleRepo.GetArticlesForCurationByRegionAsync(Arg.Any<SourceRegion>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<NewsArticle>());

        var command = new SendCurationBatchCommand();

        // Act
        var result = await SendCurationBatchCommandHandler.Handle(
            command,
            _uow,
            _telegramService,
            _telegramState,
            _options,
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.SentCount);
        await _telegramService.DidNotReceive().SendThematicBatchAsync(Arg.Any<SourceRegion>(), Arg.Any<IReadOnlyList<NewsArticle>>(), Arg.Any<CancellationToken>());
        await _telegramService.DidNotReceive().SendDigestControlPanelAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
