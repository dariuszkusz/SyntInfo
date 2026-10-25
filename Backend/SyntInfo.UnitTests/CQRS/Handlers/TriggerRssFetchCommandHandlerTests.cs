using CodeHollow.FeedReader;
using Microsoft.Extensions.Options;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class TriggerRssFetchCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsSourceRepository _newsSourceRepo = Substitute.For<INewsSourceRepository>();
    private readonly INewsArticleRepository _newsArticleRepo = Substitute.For<INewsArticleRepository>();
    private readonly IRepository<NewsArticle> _articleGenericRepo = Substitute.For<IRepository<NewsArticle>>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IOpenRouterClient _openRouterClient = Substitute.For<IOpenRouterClient>();
    private readonly IGoogleAiStudioClient _googleAiStudioClient = Substitute.For<IGoogleAiStudioClient>();
    private readonly ILogger<TriggerRssFetchCommandHandler> _logger = Substitute.For<ILogger<TriggerRssFetchCommandHandler>>();
    private readonly IRssFeedClient _rssFeedClient = Substitute.For<IRssFeedClient>();
    private readonly INewsDiscoveryService _newsDiscoveryService = Substitute.For<INewsDiscoveryService>();
    private readonly IOptions<ProcessingOptions> _options;

    public TriggerRssFetchCommandHandlerTests()
    {
        _options = Options.Create(new ProcessingOptions
        {
            MaxArticlesPerRegion = 5,
            MaxPositiveArticles = 2
        });

        _uow.NewsSources.Returns(_newsSourceRepo);
        _uow.NewsArticles.Returns(_newsArticleRepo);
        _uow.Repository<NewsArticle>().Returns(_articleGenericRepo);

        // Domyślne mocki bazodanowe i serwisowe
        _newsSourceRepo.GetActiveSourcesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<NewsSource>());

        _newsDiscoveryService.DiscoverNewsAsync(Arg.Any<SourceRegion>(), Arg.Any<CancellationToken>())
            .Returns(new List<DiscoveredNewsItem>());

        _newsArticleRepo.ExistsByUrlOrTitleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _articleGenericRepo.Query()
            .Returns(new List<NewsArticle>().BuildMock());
    }

    private TriggerRssFetchCommandHandler CreateHandler()
    {
        return new TriggerRssFetchCommandHandler(
            _uow,
            _bus,
            _openRouterClient,
            _googleAiStudioClient,
            _logger,
            _options,
            _rssFeedClient,
            _newsDiscoveryService);
    }

    [Fact]
    public async Task Handle_DeactivatesArticlesOlderThan24Hours()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var expiredArticle = new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = "Wygasły artykuł",
            IsActive = true,
            PublishedAt = DateTime.UtcNow.AddHours(-26)
        };

        var activeArticle = new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = "Aktualny artykuł",
            IsActive = true,
            PublishedAt = DateTime.UtcNow.AddHours(-2)
        };

        var articles = new List<NewsArticle> { expiredArticle, activeArticle };
        _articleGenericRepo.Query().Returns(articles.BuildMock());

        var handler = CreateHandler();

        // Act
        await handler.Handle(new TriggerRssFetchCommand(), cancellationToken);

        // Assert
        expiredArticle.IsActive.Should().BeFalse();
        activeArticle.IsActive.Should().BeTrue();
        _articleGenericRepo.Received(1).Update(expiredArticle);
        _articleGenericRepo.DidNotReceive().Update(activeArticle);
        await _uow.Received().SaveChangesAsync(cancellationToken);
    }

    [Fact]
    public async Task Handle_WhenRssReturnsItems_FiltersExistingAndDispatchesSummarizeCommands()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var source = new NewsSource
        {
            Id = Guid.NewGuid(),
            Name = "Polska Agencja Prasowa",
            RssUrl = "https://example.com/rss",
            Region = SourceRegion.Poland,
            IsActive = true,
            LastFetchedAt = DateTime.UtcNow.AddDays(-1)
        };

        _newsSourceRepo.GetActiveSourcesAsync(cancellationToken)
            .Returns(new List<NewsSource> { source });

        var feed = new Feed
        {
            Items = new List<FeedItem>
            {
                new()
                {
                    Title = "Istniejący artykuł",
                    Link = "https://example.com/art1",
                    Content = "Treść 1",
                    PublishingDate = DateTime.UtcNow
                },
                new()
                {
                    Title = "Nowy unikalny artykuł",
                    Link = "https://example.com/art2",
                    Content = "Nowa treść artykułu z feedu",
                    PublishingDate = DateTime.UtcNow
                }
            }
        };

        _rssFeedClient.FetchFeedAsync(source.RssUrl, cancellationToken)
            .Returns(feed);

        _newsArticleRepo.ExistsByUrlOrTitleAsync("https://example.com/art1", "Istniejący artykuł", cancellationToken)
            .Returns(true);
        _newsArticleRepo.ExistsByUrlOrTitleAsync("https://example.com/art2", "Nowy unikalny artykuł", cancellationToken)
            .Returns(false);

        var dummyEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        _openRouterClient.GenerateEmbeddingsAsync(Arg.Any<string>(), cancellationToken)
            .Returns(dummyEmbedding);

        var handler = CreateHandler();

        // Act
        await handler.Handle(new TriggerRssFetchCommand(), cancellationToken);

        // Assert
        _newsSourceRepo.Received(1).Update(Arg.Is<NewsSource>(s => s.Id == source.Id && s.LastFetchedAt > DateTime.UtcNow.AddMinutes(-1)));
        await _bus.Received(1).InvokeAsync(Arg.Is<SummarizeArticleCommand>(cmd =>
            cmd.Title == "Nowy unikalny artykuł" &&
            cmd.Url == "https://example.com/art2" &&
            cmd.Region == SourceRegion.Poland), cancellationToken);
    }

    [Fact]
    public async Task Handle_WhenFeedThrowsException_ContinuesProcessingOtherSources()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var failingSource = new NewsSource
        {
            Id = Guid.NewGuid(),
            Name = "Błędne źródło",
            RssUrl = "https://invalid-url.com/rss",
            Region = SourceRegion.Poland,
            IsActive = true
        };
        var successfulSource = new NewsSource
        {
            Id = Guid.NewGuid(),
            Name = "Dobre źródło",
            RssUrl = "https://valid-url.com/rss",
            Region = SourceRegion.Poland,
            IsActive = true
        };

        _newsSourceRepo.GetActiveSourcesAsync(cancellationToken)
            .Returns(new List<NewsSource> { failingSource, successfulSource });

        _rssFeedClient.FetchFeedAsync(failingSource.RssUrl, cancellationToken)
            .Returns<Feed?>(_ => throw new HttpRequestException("Połączenie odrzucone"));

        var validFeed = new Feed
        {
            Items = new List<FeedItem>
            {
                new()
                {
                    Title = "Artykuł z dobrego źródła",
                    Link = "https://valid-url.com/art1",
                    Content = "Opis",
                    PublishingDate = DateTime.UtcNow
                }
            }
        };

        _rssFeedClient.FetchFeedAsync(successfulSource.RssUrl, cancellationToken)
            .Returns(validFeed);

        _openRouterClient.GenerateEmbeddingsAsync(Arg.Any<string>(), cancellationToken)
            .Returns(new float[] { 0.5f, 0.5f });

        var handler = CreateHandler();

        // Act
        var act = async () => await handler.Handle(new TriggerRssFetchCommand(), cancellationToken);

        // Assert
        await act.Should().NotThrowAsync();
        _newsSourceRepo.Received(1).Update(Arg.Is<NewsSource>(s => s.Id == successfulSource.Id));
        await _bus.Received(1).InvokeAsync(Arg.Is<SummarizeArticleCommand>(c => c.Title == "Artykuł z dobrego źródła"), cancellationToken);
    }

    [Fact]
    public async Task Handle_WhenRegionHasNoRssArticles_InvokesDiscoveryService()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Brak źródeł RSS
        _newsSourceRepo.GetActiveSourcesAsync(cancellationToken)
            .Returns(new List<NewsSource>());

        var discoveredItem = new DiscoveredNewsItem
        {
            Title = "Odkryty news z Tavily",
            Content = "Treść odkrytego newsu",
            Url = "https://tavily.com/news/1",
            PublishedAt = DateTime.UtcNow,
            Region = SourceRegion.World
        };

        _newsDiscoveryService.DiscoverNewsAsync(SourceRegion.World, cancellationToken)
            .Returns(new List<DiscoveredNewsItem> { discoveredItem });

        _openRouterClient.GenerateEmbeddingsAsync(Arg.Any<string>(), cancellationToken)
            .Returns(new float[] { 0.2f, 0.4f, 0.6f });

        var handler = CreateHandler();

        // Act
        await handler.Handle(new TriggerRssFetchCommand(), cancellationToken);

        // Assert
        await _newsDiscoveryService.Received(1).DiscoverNewsAsync(SourceRegion.World, cancellationToken);
        await _bus.Received(1).InvokeAsync(Arg.Is<SummarizeArticleCommand>(cmd =>
            cmd.Title == "Odkryty news z Tavily" &&
            cmd.Url == "https://tavily.com/news/1" &&
            cmd.Region == SourceRegion.World), cancellationToken);
    }

    [Fact]
    public async Task Handle_WhenEmbeddingFailsForOneArticle_ContinuesWithOtherArticles()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var source = new NewsSource
        {
            Id = Guid.NewGuid(),
            Name = "Źródło",
            RssUrl = "https://example.com/rss",
            Region = SourceRegion.Poland,
            IsActive = true
        };

        _newsSourceRepo.GetActiveSourcesAsync(cancellationToken)
            .Returns(new List<NewsSource> { source });

        var feed = new Feed
        {
            Items = new List<FeedItem>
            {
                new() { Title = "Artykuł z błędem embeddingu", Link = "https://example.com/fail", Content = "Treść 1", PublishingDate = DateTime.UtcNow },
                new() { Title = "Artykuł z sukcesem embeddingu", Link = "https://example.com/ok", Content = "Treść 2", PublishingDate = DateTime.UtcNow }
            }
        };

        _rssFeedClient.FetchFeedAsync(source.RssUrl, cancellationToken).Returns(feed);

        _openRouterClient.GenerateEmbeddingsAsync(Arg.Is<string>(s => s.Contains("Artykuł z błędem")), cancellationToken)
            .Returns<float[]>(_ => throw new Exception("Błąd API OpenRouter"));

        _openRouterClient.GenerateEmbeddingsAsync(Arg.Is<string>(s => s.Contains("Artykuł z sukcesem")), cancellationToken)
            .Returns(new float[] { 0.1f, 0.2f });

        var handler = CreateHandler();

        // Act
        await handler.Handle(new TriggerRssFetchCommand(), cancellationToken);

        // Assert
        await _bus.DidNotReceive().InvokeAsync(Arg.Is<SummarizeArticleCommand>(c => c.Title == "Artykuł z błędem embeddingu"), cancellationToken);
        await _bus.Received(1).InvokeAsync(Arg.Is<SummarizeArticleCommand>(c => c.Title == "Artykuł z sukcesem embeddingu"), cancellationToken);
    }

    [Fact]
    public async Task Handle_WhenArticlesHaveHighCosineSimilarity_ClustersAndMergesIntoSingleCommand()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var source = new NewsSource
        {
            Id = Guid.NewGuid(),
            Name = "Agencja",
            RssUrl = "https://example.com/rss",
            Region = SourceRegion.Poland,
            IsActive = true
        };

        _newsSourceRepo.GetActiveSourcesAsync(cancellationToken)
            .Returns(new List<NewsSource> { source });

        var feed = new Feed
        {
            Items = new List<FeedItem>
            {
                new()
                {
                    Title = "Wielkie odkrycie naukowe",
                    Link = "https://example.com/science-short",
                    Content = "Krótki opis odkrycia",
                    PublishingDate = DateTime.UtcNow
                },
                new()
                {
                    Title = "Przełom w nauce: szczegółowy raport",
                    Link = "https://example.com/science-long",
                    Content = "Bardzo obszerny i długi opis przełomowego odkrycia naukowego naukowców",
                    PublishingDate = DateTime.UtcNow
                }
            }
        };

        _rssFeedClient.FetchFeedAsync(source.RssUrl, cancellationToken).Returns(feed);

        // Identyczne wektory -> podobieństwo kosinusowe = 1.0 (> 0.82)
        var identicalVector = new float[] { 1.0f, 0.0f, 0.0f };
        _openRouterClient.GenerateEmbeddingsAsync(Arg.Any<string>(), cancellationToken)
            .Returns(identicalVector);

        var handler = CreateHandler();

        // Act
        await handler.Handle(new TriggerRssFetchCommand(), cancellationToken);

        // Assert: 2 kandydaci powinni zostać złączeni w 1 skonsolidowany SummarizeArticleCommand
        await _bus.Received(1).InvokeAsync(Arg.Is<SummarizeArticleCommand>(cmd =>
            cmd.AdditionalUrls != null &&
            cmd.AdditionalUrls.Count == 1 &&
            cmd.Content.Contains("[NOWE MATERIAŁY ŹRÓDŁOWE]:") &&
            cmd.Content.Contains("Wielkie odkrycie naukowe") &&
            cmd.Content.Contains("Przełom w nauce: szczegółowy raport")), cancellationToken);
    }

    [Fact]
    public async Task Handle_WhenCandidateCountExceedsLimit_UsesAiSelection()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var source = new NewsSource
        {
            Id = Guid.NewGuid(),
            Name = "Wydawca",
            RssUrl = "https://example.com/rss",
            Region = SourceRegion.Poland,
            IsActive = true
        };

        _newsSourceRepo.GetActiveSourcesAsync(cancellationToken)
            .Returns(new List<NewsSource> { source });

        // Limit dla regionu to 5, generujemy 6 różnych kandydatów
        var items = new List<FeedItem>();
        for (int i = 1; i <= 6; i++)
        {
            items.Add(new FeedItem
            {
                Title = $"Artykuł #{i}",
                Link = $"https://example.com/article/{i}",
                Content = $"Treść artykułu #{i}",
                PublishingDate = DateTime.UtcNow
            });
        }

        var feed = new Feed { Items = items };
        _rssFeedClient.FetchFeedAsync(source.RssUrl, cancellationToken).Returns(feed);

        // Różne wektory ortogonalne, by nie klastrowały się w jeden
        int vectorIdx = 0;
        _openRouterClient.GenerateEmbeddingsAsync(Arg.Any<string>(), cancellationToken)
            .Returns(_ =>
            {
                var vec = new float[10];
                vec[vectorIdx++ % 10] = 1.0f;
                return vec;
            });

        // AI wybiera indeks 0, 2, 4
        _googleAiStudioClient.SelectTopArticlesIndexesAsync(Arg.Any<string>(), 5, SourceRegion.Poland, cancellationToken)
            .Returns(new List<int> { 0, 2, 4 });

        var handler = CreateHandler();

        // Act
        await handler.Handle(new TriggerRssFetchCommand(), cancellationToken);

        // Assert
        await _googleAiStudioClient.Received(1).SelectTopArticlesIndexesAsync(Arg.Any<string>(), 5, SourceRegion.Poland, cancellationToken);
        // Powinny zostać wysłane dokładnie 3 wybrane przez AI komendy
        await _bus.Received(3).InvokeAsync(Arg.Any<SummarizeArticleCommand>(), cancellationToken);
    }
}
