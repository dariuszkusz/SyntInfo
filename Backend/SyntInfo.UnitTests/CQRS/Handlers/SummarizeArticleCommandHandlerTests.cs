using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class SummarizeArticleCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _newsArticleRepo = Substitute.For<INewsArticleRepository>();
    private readonly IRepository<NewsCategory> _categoryRepo = Substitute.For<IRepository<NewsCategory>>();
    private readonly IGoogleAiStudioClient _googleAiStudioClient = Substitute.For<IGoogleAiStudioClient>();
    private readonly IOpenRouterClient _openRouterClient = Substitute.For<IOpenRouterClient>();
    private readonly ISearchService _searchService = Substitute.For<ISearchService>();
    private readonly IWebMetadataExtractor _webMetadataExtractor = Substitute.For<IWebMetadataExtractor>();
    private readonly ILogger<SummarizeArticleCommandHandler> _logger = Substitute.For<ILogger<SummarizeArticleCommandHandler>>();

    public SummarizeArticleCommandHandlerTests()
    {
        _uow.NewsArticles.Returns(_newsArticleRepo);
        _uow.Repository<NewsCategory>().Returns(_categoryRepo);

        _categoryRepo.Query().Returns(new List<NewsCategory>().BuildMock());
        _searchService.SearchDetailedWithImageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResultInfo { Content = "Search content", ImageUrl = "https://example.com/img.jpg" });
        _googleAiStudioClient.GenerateFactsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("{\"facts\": [\"Fakt 1\"]}");
    }

    private SummarizeArticleCommandHandler CreateHandler()
    {
        return new SummarizeArticleCommandHandler(
            _uow,
            _openRouterClient,
            _googleAiStudioClient,
            _searchService,
            _webMetadataExtractor,
            _logger);
    }

    [Fact]
    public async Task Handle_WhenEditorReturnsSafetyFilterMessage_DoesNotSaveArticle()
    {
        // Arrange
        var command = new SummarizeArticleCommand(
            Title: "Groźna pogoda",
            Content: "Treść o pogodzie",
            Url: "https://example.com/weather",
            PublishedAt: DateTime.UtcNow,
            SourceId: Guid.NewGuid(),
            Region: SourceRegion.Poland
        );

        // Redaktor zwraca komunikat moderacyjny zamiast JSON
        _googleAiStudioClient.GenerateSummaryFromFactsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("User Safety: safe");

        var handler = CreateHandler();

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert - artykuł NIE został zapisany
        await _newsArticleRepo.DidNotReceive().AddAsync(Arg.Any<NewsArticle>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenEditorReturnsValidJson_SavesArticle()
    {
        // Arrange
        var command = new SummarizeArticleCommand(
            Title: "Ważne odkrycie",
            Content: "Treść artykułu",
            Url: "https://example.com/science",
            PublishedAt: DateTime.UtcNow,
            SourceId: Guid.NewGuid(),
            Region: SourceRegion.Poland
        );

        var validJson = "{\"title\": \"Skrócony tytuł\", \"essence\": \"To jest bardzo rzetelne i wartościowe streszczenie artykułu informacyjnego.\", \"category\": \"Nauka\"}";
        _googleAiStudioClient.GenerateSummaryFromFactsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(validJson);
        _openRouterClient.GenerateEmbeddingsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new float[] { 0.1f, 0.2f });

        var handler = CreateHandler();

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert - artykuł został poprawnie zapisany
        await _newsArticleRepo.Received(1).AddAsync(Arg.Is<NewsArticle>(a =>
            a.Title == "Skrócony tytuł" &&
            a.SummaryText.Contains("rzetelne i wartościowe")), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
