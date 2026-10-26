using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class PublishWykopNewsCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _newsArticleRepo = Substitute.For<INewsArticleRepository>();
    private readonly ISocialPostGenerator _postGenerator = Substitute.For<ISocialPostGenerator>();
    private readonly IWykopClient _wykopClient = Substitute.For<IWykopClient>();
    private readonly ITelegramNotificationService _telegramService = Substitute.For<ITelegramNotificationService>();
    private readonly IOptions<CurationOptions> _curationOptions = Microsoft.Extensions.Options.Options.Create(new CurationOptions { MaxArticlesPerRun = 5 });
    private readonly ILogger<PublishWykopNewsCommand> _logger = Substitute.For<ILogger<PublishWykopNewsCommand>>();

    public PublishWykopNewsCommandHandlerTests()
    {
        _uow.NewsArticles.Returns(_newsArticleRepo);
    }

    [Fact]
    public async Task Handle_WhenNoActiveArticles_ReturnsFailureResult()
    {
        // Arrange
        var command = new PublishWykopNewsCommand();
        _newsArticleRepo.GetActiveArticlesAsync(5, Arg.Any<CancellationToken>())
            .Returns(new List<NewsArticle>());

        // Act
        var result = await PublishWykopNewsCommandHandler.Handle(
            command,
            _uow,
            _postGenerator,
            _wykopClient,
            _telegramService,
            _curationOptions,
            _logger,
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, result.PublishedArticlesCount);
        Assert.Null(result.EntryId);
        await _wykopClient.DidNotReceiveWithAnyArgs().CreateEntryAsync(default!, default, default);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Handle_WhenActiveArticlesFound_PublishesToWykopAndUpdatesArticles()
    {
        // Arrange
        var command = new PublishWykopNewsCommand(5);
        var articles = new List<NewsArticle>
        {
            new() { Id = Guid.NewGuid(), Title = "Wiadomość 1", SummaryText = "Podsumowanie 1", ImageUrl = "https://example.com/1.jpg" },
            new() { Id = Guid.NewGuid(), Title = "Wiadomość 2", SummaryText = "Podsumowanie 2" },
            new() { Id = Guid.NewGuid(), Title = "Wiadomość 3", SummaryText = "Podsumowanie 3" },
            new() { Id = Guid.NewGuid(), Title = "Wiadomość 4", SummaryText = "Podsumowanie 4" },
            new() { Id = Guid.NewGuid(), Title = "Wiadomość 5", SummaryText = "Podsumowanie 5" }
        };

        _newsArticleRepo.GetActiveArticlesAsync(5, Arg.Any<CancellationToken>())
            .Returns(articles);

        _postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Wykop, Arg.Any<IReadOnlyList<NewsArticle>>(), Arg.Any<CancellationToken>())
            .Returns("Wykop digest content");

        _wykopClient.UploadPhotoAsync("https://example.com/1.jpg", Arg.Any<CancellationToken>())
            .Returns("photo-key-123");

        _wykopClient.CreateEntryAsync("Wykop digest content", "photo-key-123", Arg.Any<CancellationToken>())
            .Returns("wykop-entry-999");

        // Act
        var result = await PublishWykopNewsCommandHandler.Handle(
            command,
            _uow,
            _postGenerator,
            _wykopClient,
            _telegramService,
            _curationOptions,
            _logger,
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.PublishedArticlesCount);
        Assert.Equal("wykop-entry-999", result.EntryId);

        await _wykopClient.Received(1).CreateEntryAsync("Wykop digest content", "photo-key-123", Arg.Any<CancellationToken>());
        _newsArticleRepo.Received(5).Update(Arg.Any<NewsArticle>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _telegramService.Received(1).SendSimpleNotificationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWykopThrowsException_ReturnsFailureResult()
    {
        // Arrange
        var command = new PublishWykopNewsCommand();
        var articles = new List<NewsArticle>
        {
            new() { Id = Guid.NewGuid(), Title = "Wiadomość 1", SummaryText = "Podsumowanie 1" }
        };

        _newsArticleRepo.GetActiveArticlesAsync(5, Arg.Any<CancellationToken>())
            .Returns(articles);

        _postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Wykop, Arg.Any<IReadOnlyList<NewsArticle>>(), Arg.Any<CancellationToken>())
            .Returns("Wykop digest content");

        _wykopClient.CreateEntryAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Wykop API error 500"));

        // Act
        var result = await PublishWykopNewsCommandHandler.Handle(
            command,
            _uow,
            _postGenerator,
            _wykopClient,
            _telegramService,
            _curationOptions,
            _logger,
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Wykop API error 500", result.Error);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
