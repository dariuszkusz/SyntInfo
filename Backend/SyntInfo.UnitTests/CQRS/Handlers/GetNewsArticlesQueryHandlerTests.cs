using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class GetNewsArticlesQueryHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _newsArticleRepo = Substitute.For<INewsArticleRepository>();

    public GetNewsArticlesQueryHandlerTests()
    {
        _uow.NewsArticles.Returns(_newsArticleRepo);
    }

    [Fact]
    public async Task Handle_CallsRepositoryGetRecentArticlesAsyncAndMapsToDto()
    {
        // Arrange
        var article = new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = "Tytuł artykułu",
            OriginalTitle = "Oryginalny tytuł",
            SummaryText = "To jest pełnoprawne podsumowanie wiadomości.",
            PublishedAt = DateTime.UtcNow,
            SourceUrls = new List<string> { "https://example.com/art" },
            Category = new NewsCategory { Name = "Polska" },
            Region = SourceRegion.Poland,
            ImageUrl = "https://example.com/img.jpg"
        };

        _newsArticleRepo.GetRecentArticlesAsync(SourceRegion.Poland, 1, 10, Arg.Any<CancellationToken>())
            .Returns(new List<NewsArticle> { article });

        var handler = new GetNewsArticlesQueryHandler(_uow);
        var query = new GetNewsArticlesQuery(Region: SourceRegion.Poland, Page: 1, PageSize: 10);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        await _newsArticleRepo.Received(1).GetRecentArticlesAsync(SourceRegion.Poland, 1, 10, Arg.Any<CancellationToken>());
        result.Should().HaveCount(1);
        var dto = result.Single();
        dto.Id.Should().Be(article.Id);
        dto.Title.Should().Be(article.Title);
        dto.SummaryText.Should().Be(article.SummaryText);
        dto.CategoryName.Should().Be("Polska");
        dto.Region.Should().Be(SourceRegion.Poland);
        dto.ImageUrl.Should().Be(article.ImageUrl);
    }
}
