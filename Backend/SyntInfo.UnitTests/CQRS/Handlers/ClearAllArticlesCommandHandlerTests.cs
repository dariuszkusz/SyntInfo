using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class ClearAllArticlesCommandHandlerTests
{
    [Fact]
    public async Task Handle_DeletesAllArticlesAndResetsNewsSourcesLastFetchedAt()
    {
        // Arrange
        var uow = Substitute.For<IUnitOfWork>();
        var articleRepo = Substitute.For<INewsArticleRepository>();
        var sourceRepo = Substitute.For<INewsSourceRepository>();
        var logger = Substitute.For<ILogger<ClearAllArticlesCommandHandler>>();

        uow.NewsArticles.Returns(articleRepo);
        uow.NewsSources.Returns(sourceRepo);

        var source1 = new NewsSource { Id = Guid.NewGuid(), Name = "PAP", LastFetchedAt = DateTime.UtcNow };
        var source2 = new NewsSource { Id = Guid.NewGuid(), Name = "Reuters", LastFetchedAt = DateTime.UtcNow.AddHours(-2) };
        sourceRepo.GetAllSourcesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<NewsSource> { source1, source2 });

        var handler = new ClearAllArticlesCommandHandler(uow, logger);

        // Act
        await handler.Handle(new ClearAllArticlesCommand(), CancellationToken.None);

        // Assert
        await articleRepo.Received(1).DeleteAllAsync(Arg.Any<CancellationToken>());
        source1.LastFetchedAt.Should().Be(DateTime.MinValue);
        source2.LastFetchedAt.Should().Be(DateTime.MinValue);
        sourceRepo.Received(1).Update(source1);
        sourceRepo.Received(1).Update(source2);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}

