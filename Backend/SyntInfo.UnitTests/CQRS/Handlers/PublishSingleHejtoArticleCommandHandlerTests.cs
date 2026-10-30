using FluentAssertions;
using Microsoft.Extensions.Logging;
using MockQueryable.NSubstitute;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Xunit;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class PublishSingleHejtoArticleCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _newsArticleRepo = Substitute.For<INewsArticleRepository>();
    private readonly IHejtoClient _hejtoClient = Substitute.For<IHejtoClient>();
    private readonly ILogger<PublishSingleHejtoArticleCommand> _logger = Substitute.For<ILogger<PublishSingleHejtoArticleCommand>>();

    public PublishSingleHejtoArticleCommandHandlerTests()
    {
        _uow.NewsArticles.Returns(_newsArticleRepo);
    }

    [Fact]
    public async Task Handle_WhenArticleNotFound_LogsWarningAndDoesNotCallHejto()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var emptyArticles = new List<NewsArticle>().BuildMock();
        _newsArticleRepo.Query().Returns(emptyArticles);

        var command = new PublishSingleHejtoArticleCommand(articleId);

        // Act
        await PublishSingleHejtoArticleCommandHandler.Handle(
            command,
            _uow,
            _hejtoClient,
            _logger,
            CancellationToken.None);

        // Assert
        await _hejtoClient.DidNotReceiveWithAnyArgs().CreatePostAsync(default!, default, default, default);
        await _hejtoClient.DidNotReceiveWithAnyArgs().UploadImageAsync(default!, default);
    }

    [Fact]
    public async Task Handle_WhenArticleHasImage_UploadsImageAndCreatesPost()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new NewsArticle
        {
            Id = articleId,
            Title = "Nowa inwestycja w technologie",
            SummaryText = "Szczegółowy opis nowej inwestycji w technologie przyszłości.",
            ImageUrl = "https://example.com/tech.jpg",
            SourceUrls = new List<string> { "https://example.com/source" },
            Region = SourceRegion.Poland
        };

        var articlesQueryable = new List<NewsArticle> { article }.BuildMock();
        _newsArticleRepo.Query().Returns(articlesQueryable);

        _hejtoClient.UploadImageAsync("https://example.com/tech.jpg", Arg.Any<CancellationToken>())
            .Returns("hejto-img-uuid-123");

        _hejtoClient.CreatePostAsync(
                Arg.Is<string>(s => s.Contains("Nowa inwestycja")),
                "wiadomosci-polska",
                "hejto-img-uuid-123",
                Arg.Any<CancellationToken>())
            .Returns("hejto-post-slug-1");

        var command = new PublishSingleHejtoArticleCommand(articleId, "wiadomosci-polska", 1, 5);

        // Act
        await PublishSingleHejtoArticleCommandHandler.Handle(
            command,
            _uow,
            _hejtoClient,
            _logger,
            CancellationToken.None);

        // Assert
        await _hejtoClient.Received(1).UploadImageAsync("https://example.com/tech.jpg", Arg.Any<CancellationToken>());
        await _hejtoClient.Received(1).CreatePostAsync(
            Arg.Is<string>(s => s.Contains("Nowa inwestycja")),
            "wiadomosci-polska",
            "hejto-img-uuid-123",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenImageUploadFails_ContinuesWithoutAttachmentAndCreatesPost()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new NewsArticle
        {
            Id = articleId,
            Title = "Artykuł z błędnym obrazkiem",
            SummaryText = "Treść podsumowania artykułu.",
            ImageUrl = "https://example.com/broken.jpg"
        };

        var articlesQueryable = new List<NewsArticle> { article }.BuildMock();
        _newsArticleRepo.Query().Returns(articlesQueryable);

        _hejtoClient.UploadImageAsync("https://example.com/broken.jpg", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("404 Not Found"));

        _hejtoClient.CreatePostAsync(Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns("hejto-post-fallback");

        var command = new PublishSingleHejtoArticleCommand(articleId);

        // Act
        await PublishSingleHejtoArticleCommandHandler.Handle(
            command,
            _uow,
            _hejtoClient,
            _logger,
            CancellationToken.None);

        // Assert
        await _hejtoClient.Received(1).CreatePostAsync(Arg.Any<string>(), null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenHejtoCreatePostFails_RethrowsException()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new NewsArticle
        {
            Id = articleId,
            Title = "Błędny wpis",
            SummaryText = "Treść podsumowania."
        };

        var articlesQueryable = new List<NewsArticle> { article }.BuildMock();
        _newsArticleRepo.Query().Returns(articlesQueryable);

        _hejtoClient.CreatePostAsync(Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("500 Server Error"));

        var command = new PublishSingleHejtoArticleCommand(articleId);

        // Act & Assert
        var act = () => PublishSingleHejtoArticleCommandHandler.Handle(
            command,
            _uow,
            _hejtoClient,
            _logger,
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
