using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class PublishHejtoArticlesSeparatelyCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _newsArticleRepo = Substitute.For<INewsArticleRepository>();
    private readonly IHejtoClient _hejtoClient = Substitute.For<IHejtoClient>();
    private readonly IOptions<CurationOptions> _curationOptions = Options.Create(new CurationOptions { MaxArticlesPerRun = 5 });
    private readonly IOptions<HejtoOptions> _hejtoOptions = Options.Create(new HejtoOptions { DelayBetweenPostsMs = 0 });
    private readonly ILogger<PublishHejtoArticlesSeparatelyCommand> _logger = Substitute.For<ILogger<PublishHejtoArticlesSeparatelyCommand>>();

    public PublishHejtoArticlesSeparatelyCommandHandlerTests()
    {
        _uow.NewsArticles.Returns(_newsArticleRepo);
    }

    [Fact]
    public async Task Handle_WhenNoArticlesAvailable_ReturnsFailureResult()
    {
        // Arrange
        var command = new PublishHejtoArticlesSeparatelyCommand();
        _newsArticleRepo.GetSelectedForDigestAsync(Arg.Any<CancellationToken>())
            .Returns(new List<NewsArticle>());
        _newsArticleRepo.GetActiveArticlesAsync(5, Arg.Any<CancellationToken>())
            .Returns(new List<NewsArticle>());

        // Act
        var result = await PublishHejtoArticlesSeparatelyCommandHandler.Handle(
            command,
            _uow,
            _hejtoClient,
            _curationOptions,
            _hejtoOptions,
            _logger,
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.PublishedCount.Should().Be(0);
        await _hejtoClient.DidNotReceiveWithAnyArgs().CreatePostAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Handle_WhenArticlesHaveImages_UploadsAttachmentAndPublishesEachArticleSeparately()
    {
        // Arrange
        var command = new PublishHejtoArticlesSeparatelyCommand(Count: 2, CommunitySlug: "wiadomosci-polska");
        var articles = new List<NewsArticle>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Artykuł pierwszy",
                SummaryText = "Podsumowanie artykułu pierwszego.",
                ImageUrl = "https://example.com/img1.jpg",
                SourceUrls = new List<string> { "https://source1.pl/art1" },
                Region = SourceRegion.Poland
            },
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Artykuł drugi bez obrazka",
                SummaryText = "Podsumowanie artykułu drugiego.",
                ImageUrl = null,
                SourceUrls = new List<string> { "https://source2.pl/art2" },
                Region = SourceRegion.World
            }
        };

        _newsArticleRepo.GetSelectedForDigestAsync(Arg.Any<CancellationToken>())
            .Returns(articles);

        _hejtoClient.UploadImageAsync("https://example.com/img1.jpg", Arg.Any<CancellationToken>())
            .Returns("uuid-img-1");

        _hejtoClient.CreatePostAsync(
                Arg.Is<string>(s => s.Contains("Artykuł pierwszy")),
                "wiadomosci-polska",
                "uuid-img-1",
                Arg.Any<CancellationToken>())
            .Returns("hejto-post-1");

        _hejtoClient.CreatePostAsync(
                Arg.Is<string>(s => s.Contains("Artykuł drugi bez obrazka")),
                "wiadomosci-polska",
                null,
                Arg.Any<CancellationToken>())
            .Returns("hejto-post-2");

        // Act
        var result = await PublishHejtoArticlesSeparatelyCommandHandler.Handle(
            command,
            _uow,
            _hejtoClient,
            _curationOptions,
            _hejtoOptions,
            _logger,
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalArticlesCount.Should().Be(2);
        result.PublishedCount.Should().Be(2);
        result.PostIds.Should().BeEquivalentTo(new[] { "hejto-post-1", "hejto-post-2" });

        await _hejtoClient.Received(1).UploadImageAsync("https://example.com/img1.jpg", Arg.Any<CancellationToken>());
        await _hejtoClient.Received(2).CreatePostAsync(Arg.Any<string>(), "wiadomosci-polska", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenImageUploadFails_StillPublishesArticleWithoutAttachment()
    {
        // Arrange
        var command = new PublishHejtoArticlesSeparatelyCommand();
        var articles = new List<NewsArticle>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Artykuł z błędnym obrazkiem",
                SummaryText = "Treść podsumowania artykułu.",
                ImageUrl = "https://example.com/broken.jpg"
            }
        };

        _newsArticleRepo.GetSelectedForDigestAsync(Arg.Any<CancellationToken>())
            .Returns(articles);

        _hejtoClient.UploadImageAsync("https://example.com/broken.jpg", Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("404 Not Found"));

        _hejtoClient.CreatePostAsync(Arg.Any<string>(), null, null, Arg.Any<CancellationToken>())
            .Returns("hejto-post-fallback");

        // Act
        var result = await PublishHejtoArticlesSeparatelyCommandHandler.Handle(
            command,
            _uow,
            _hejtoClient,
            _curationOptions,
            _hejtoOptions,
            _logger,
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.PublishedCount.Should().Be(1);
        result.PostIds.Should().ContainSingle().Which.Should().Be("hejto-post-fallback");
        await _hejtoClient.Received(1).CreatePostAsync(Arg.Any<string>(), null, null, Arg.Any<CancellationToken>());
    }
}
