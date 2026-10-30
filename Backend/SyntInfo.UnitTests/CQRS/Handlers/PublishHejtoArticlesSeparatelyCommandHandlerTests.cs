using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.NSubstitute;
using NSubstitute;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine;
using Xunit;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class PublishHejtoArticlesSeparatelyCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _newsArticleRepo = Substitute.For<INewsArticleRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IOptions<CurationOptions> _curationOptions = Options.Create(new CurationOptions { MaxArticlesPerRun = 5 });
    private readonly IOptions<HejtoOptions> _hejtoOptions = Options.Create(new HejtoOptions { DelayBetweenPostsSeconds = 60 });
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
            _bus,
            _curationOptions,
            _hejtoOptions,
            _logger,
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.PublishedCount.Should().Be(0);
        await _bus.DidNotReceive().PublishAsync(
            Arg.Any<PublishSingleHejtoArticleCommand>(),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_WhenArticlesAvailable_SchedulesEachArticleWithIncrementalDelay_AndDoesNotBlock()
    {
        // Arrange
        var command = new PublishHejtoArticlesSeparatelyCommand(CommunitySlug: "wiadomosci-polska");
        var art1 = new NewsArticle { Id = Guid.NewGuid(), Title = "Artykuł 1", SummaryText = "Treść 1", SourceUrls = new List<string> { "https://s1.pl" } };
        var art2 = new NewsArticle { Id = Guid.NewGuid(), Title = "Artykuł 2", SummaryText = "Treść 2", SourceUrls = new List<string> { "https://s2.pl" } };
        var art3 = new NewsArticle { Id = Guid.NewGuid(), Title = "Artykuł 3", SummaryText = "Treść 3", SourceUrls = new List<string> { "https://s3.pl" } };
        var articles = new List<NewsArticle> { art1, art2, art3 };

        _newsArticleRepo.GetSelectedForDigestAsync(Arg.Any<CancellationToken>())
            .Returns(articles);

        // Act
        var result = await PublishHejtoArticlesSeparatelyCommandHandler.Handle(
            command,
            _uow,
            _bus,
            _curationOptions,
            _hejtoOptions,
            _logger,
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalArticlesCount.Should().Be(3);
        result.PublishedCount.Should().Be(3);
        result.Message.Should().Contain("co 60s");

        await _bus.Received(1).PublishAsync(
            Arg.Is<PublishSingleHejtoArticleCommand>(c => c.ArticleId == art1.Id && c.CommunitySlug == "wiadomosci-polska" && c.Index == 1 && c.TotalCount == 3),
            Arg.Is<DeliveryOptions>(o => o.ScheduleDelay == TimeSpan.Zero));

        await _bus.Received(1).PublishAsync(
            Arg.Is<PublishSingleHejtoArticleCommand>(c => c.ArticleId == art2.Id && c.CommunitySlug == "wiadomosci-polska" && c.Index == 2 && c.TotalCount == 3),
            Arg.Is<DeliveryOptions>(o => o.ScheduleDelay == TimeSpan.FromSeconds(60)));

        await _bus.Received(1).PublishAsync(
            Arg.Is<PublishSingleHejtoArticleCommand>(c => c.ArticleId == art3.Id && c.CommunitySlug == "wiadomosci-polska" && c.Index == 3 && c.TotalCount == 3),
            Arg.Is<DeliveryOptions>(o => o.ScheduleDelay == TimeSpan.FromSeconds(120)));
    }

    [Fact]
    public async Task Handle_WhenArticleIdsProvided_PreservesGivenOrder()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var art1 = new NewsArticle { Id = id1, Title = "Art 1", SummaryText = "T1" };
        var art2 = new NewsArticle { Id = id2, Title = "Art 2", SummaryText = "T2" };

        var command = new PublishHejtoArticlesSeparatelyCommand(ArticleIds: new[] { id2, id1 });
        var articlesQueryable = new List<NewsArticle> { art1, art2 }.BuildMock();
        _newsArticleRepo.Query().Returns(articlesQueryable);

        // Act
        var result = await PublishHejtoArticlesSeparatelyCommandHandler.Handle(
            command,
            _uow,
            _bus,
            _curationOptions,
            _hejtoOptions,
            _logger,
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.PublishedCount.Should().Be(2);

        // id2 powinien być zaplanowany jako pierwszy (delay 0s), a id1 jako drugi (delay 60s)
        await _bus.Received(1).PublishAsync(
            Arg.Is<PublishSingleHejtoArticleCommand>(c => c.ArticleId == id2 && c.Index == 1),
            Arg.Is<DeliveryOptions>(o => o.ScheduleDelay == TimeSpan.Zero));

        await _bus.Received(1).PublishAsync(
            Arg.Is<PublishSingleHejtoArticleCommand>(c => c.ArticleId == id1 && c.Index == 2),
            Arg.Is<DeliveryOptions>(o => o.ScheduleDelay == TimeSpan.FromSeconds(60)));
    }
}
