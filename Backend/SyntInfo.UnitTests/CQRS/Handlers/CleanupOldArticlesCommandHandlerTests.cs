using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class CleanupOldArticlesCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IRepository<NewsArticle> _articleRepo = Substitute.For<IRepository<NewsArticle>>();
    private readonly ILogger<CleanupOldArticlesCommandHandler> _logger = Substitute.For<ILogger<CleanupOldArticlesCommandHandler>>();

    public CleanupOldArticlesCommandHandlerTests()
    {
        _uow.Repository<NewsArticle>().Returns(_articleRepo);
    }

    [Fact]
    public async Task Handle_WhenOldArticlesExist_DeletesEachOldArticleAndSavesChanges()
    {
        // Arrange
        var command = new CleanupOldArticlesCommand(DaysToKeep: 7);
        var cancellationToken = CancellationToken.None;

        var oldArticle1 = new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = "Stary artykuł 1",
            PublishedAt = DateTime.UtcNow.AddDays(-10)
        };
        var oldArticle2 = new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = "Stary artykuł 2",
            PublishedAt = DateTime.UtcNow.AddDays(-8)
        };
        var recentArticle = new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = "Świeży artykuł",
            PublishedAt = DateTime.UtcNow.AddDays(-2)
        };

        var articlesList = new List<NewsArticle> { oldArticle1, oldArticle2, recentArticle };
        var mockDbSet = articlesList.BuildMock();

        _articleRepo.Query().Returns(mockDbSet);

        var handler = new CleanupOldArticlesCommandHandler(_uow, _logger);

        // Act
        await handler.Handle(command, cancellationToken);

        // Assert
        _articleRepo.Received(1).Delete(oldArticle1);
        _articleRepo.Received(1).Delete(oldArticle2);
        _articleRepo.DidNotReceive().Delete(recentArticle);
        await _uow.Received(1).SaveChangesAsync(cancellationToken);
    }

    [Fact]
    public async Task Handle_WhenNoOldArticlesFound_DoesNotDeleteNorSaveChanges()
    {
        // Arrange
        var command = new CleanupOldArticlesCommand(DaysToKeep: 7);
        var cancellationToken = CancellationToken.None;

        var recentArticle = new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = "Świeży artykuł",
            PublishedAt = DateTime.UtcNow.AddDays(-1)
        };

        var articlesList = new List<NewsArticle> { recentArticle };
        var mockDbSet = articlesList.BuildMock();

        _articleRepo.Query().Returns(mockDbSet);

        var handler = new CleanupOldArticlesCommandHandler(_uow, _logger);

        // Act
        await handler.Handle(command, cancellationToken);

        // Assert
        _articleRepo.DidNotReceive().Delete(Arg.Any<NewsArticle>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
