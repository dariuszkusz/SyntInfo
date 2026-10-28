using Microsoft.Extensions.Options;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class ToggleArticleDigestSelectionCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _articleRepo = Substitute.For<INewsArticleRepository>();
    private readonly ITelegramNotificationService _telegramService = Substitute.For<ITelegramNotificationService>();
    private readonly ITelegramCurationStateService _telegramState = Substitute.For<ITelegramCurationStateService>();
    private readonly IOptions<CurationOptions> _options = Options.Create(new CurationOptions
    {
        MaxPolandArticles = 5,
        MaxWorldArticles = 5,
        MaxPositiveArticles = 3,
        ProposalPolandCount = 10,
        ProposalWorldCount = 10,
        ProposalPositiveCount = 6
    });
    private readonly ILogger<ToggleArticleDigestSelectionCommand> _logger = Substitute.For<ILogger<ToggleArticleDigestSelectionCommand>>();

    public ToggleArticleDigestSelectionCommandHandlerTests()
    {
        _uow.NewsArticles.Returns(_articleRepo);
    }

    [Fact]
    public async Task Handle_WhenLimitExceededForPoland_DoesNotToggleAndSendsAlert()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var targetArticle = new NewsArticle
        {
            Id = articleId,
            Title = "Kandydat Polska 6",
            Region = SourceRegion.Poland,
            IsSelectedForDigest = false,
            DigestPublishedAt = null
        };

        _articleRepo.GetByIdAsync(articleId, Arg.Any<CancellationToken>()).Returns(targetArticle);

        // Symulacja, że w bazie jest już 5 zaznaczonych artykułów z Polski
        var existingSelected = Enumerable.Range(1, 5).Select(i => new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = $"Polska {i}",
            Region = SourceRegion.Poland,
            IsSelectedForDigest = true,
            DigestPublishedAt = null
        }).ToList();

        _articleRepo.Query().Returns(existingSelected.BuildMock());

        var command = new ToggleArticleDigestSelectionCommand(
            ArticleId: articleId,
            TelegramChatId: 12345,
            TelegramMessageId: 555,
            CallbackQueryId: "query-999");

        // Act
        await ToggleArticleDigestSelectionCommandHandler.Handle(
            command,
            _uow,
            _telegramService,
            _telegramState,
            _options,
            _logger,
            CancellationToken.None);

        // Assert
        Assert.False(targetArticle.IsSelectedForDigest);
        _articleRepo.DidNotReceive().Update(targetArticle);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _telegramService.Received(1).AnswerCallbackQueryAsync(
            "query-999",
            Arg.Is<string>(s => s.Contains("Osiągnięto limit")),
            showAlert: true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUnderLimit_TogglesSelectionAndUpdatesTelegramMarkup()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var targetArticle = new NewsArticle
        {
            Id = articleId,
            Title = "Kandydat Polska 1",
            Region = SourceRegion.Poland,
            IsSelectedForDigest = false,
            DigestPublishedAt = null
        };

        _articleRepo.GetByIdAsync(articleId, Arg.Any<CancellationToken>()).Returns(targetArticle);

        // Tylko 2 wybrane wcześniej
        var existingSelected = Enumerable.Range(1, 2).Select(i => new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = $"Polska {i}",
            Region = SourceRegion.Poland,
            IsSelectedForDigest = true,
            DigestPublishedAt = null
        }).ToList();

        _articleRepo.Query().Returns(existingSelected.BuildMock());
        _articleRepo.GetArticlesForCurationByRegionAsync(SourceRegion.Poland, 10, Arg.Any<CancellationToken>())
            .Returns(new List<NewsArticle> { targetArticle });

        _telegramState.GetCategoryMessageId(SourceRegion.Poland).Returns(555L);
        _telegramState.ControlPanelMessageId.Returns(999L);

        var command = new ToggleArticleDigestSelectionCommand(
            ArticleId: articleId,
            TelegramChatId: 12345,
            TelegramMessageId: 555,
            CallbackQueryId: "query-111");

        // Act
        await ToggleArticleDigestSelectionCommandHandler.Handle(
            command,
            _uow,
            _telegramService,
            _telegramState,
            _options,
            _logger,
            CancellationToken.None);

        // Assert
        Assert.True(targetArticle.IsSelectedForDigest);
        _articleRepo.Received(1).Update(targetArticle);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _telegramService.Received(1).AnswerCallbackQueryAsync(
            "query-111",
            Arg.Is<string>(s => s.Contains("Wybrano")),
            showAlert: false,
            Arg.Any<CancellationToken>());
        await _telegramService.Received(1).UpdateThematicBatchMarkupAsync(
            555,
            SourceRegion.Poland,
            Arg.Any<IReadOnlyList<NewsArticle>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAlreadySelected_UnselectsArticleRegardlessOfLimit()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var targetArticle = new NewsArticle
        {
            Id = articleId,
            Title = "Wybrany artykuł",
            Region = SourceRegion.Poland,
            IsSelectedForDigest = true,
            DigestPublishedAt = null
        };

        _articleRepo.GetByIdAsync(articleId, Arg.Any<CancellationToken>()).Returns(targetArticle);

        var articlesList = new List<NewsArticle> { targetArticle };
        _articleRepo.Query().Returns(articlesList.BuildMock());
        _articleRepo.GetArticlesForCurationByRegionAsync(SourceRegion.Poland, 10, Arg.Any<CancellationToken>())
            .Returns(articlesList);

        _telegramState.GetCategoryMessageId(SourceRegion.Poland).Returns(555L);

        var command = new ToggleArticleDigestSelectionCommand(
            ArticleId: articleId,
            TelegramChatId: 12345,
            TelegramMessageId: 555,
            CallbackQueryId: "query-222");

        // Act
        await ToggleArticleDigestSelectionCommandHandler.Handle(
            command,
            _uow,
            _telegramService,
            _telegramState,
            _options,
            _logger,
            CancellationToken.None);

        // Assert
        Assert.False(targetArticle.IsSelectedForDigest);
        _articleRepo.Received(1).Update(targetArticle);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _telegramService.Received(1).AnswerCallbackQueryAsync(
            "query-222",
            Arg.Is<string>(s => s.Contains("Usunięto")),
            showAlert: false,
            Arg.Any<CancellationToken>());
    }
}
