using Microsoft.Extensions.Options;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Application.DTOs;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using Wolverine;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class GetTopNewsQueryHandlerTests
{
    [Fact]
    public async Task Handle_InvokesBusForPolandWorldAndPositiveRegionsWithConfiguredLimits()
    {
        // Arrange
        var bus = Substitute.For<IMessageBus>();
        var options = Options.Create(new ProcessingOptions
        {
            MaxArticlesPerRegion = 6,
            MaxPositiveArticles = 3
        });

        var polandList = new List<NewsArticleDto> { new() { Id = Guid.NewGuid(), Title = "PL" } };
        var worldList = new List<NewsArticleDto> { new() { Id = Guid.NewGuid(), Title = "World" } };
        var positiveList = new List<NewsArticleDto> { new() { Id = Guid.NewGuid(), Title = "Positive" } };

        bus.InvokeAsync<List<NewsArticleDto>>(
                Arg.Is<GetNewsArticlesQuery>(q => q.Region == SourceRegion.Poland && q.PageSize == 6),
                Arg.Any<CancellationToken>())
            .Returns(polandList);

        bus.InvokeAsync<List<NewsArticleDto>>(
                Arg.Is<GetNewsArticlesQuery>(q => q.Region == SourceRegion.World && q.PageSize == 6),
                Arg.Any<CancellationToken>())
            .Returns(worldList);

        bus.InvokeAsync<List<NewsArticleDto>>(
                Arg.Is<GetNewsArticlesQuery>(q => q.Region == SourceRegion.Positive && q.PageSize == 3),
                Arg.Any<CancellationToken>())
            .Returns(positiveList);

        var handler = new GetTopNewsQueryHandler(bus, options);

        // Act
        var response = await handler.Handle(new GetTopNewsQuery(), CancellationToken.None);

        // Assert
        response.Poland.Should().BeSameAs(polandList);
        response.World.Should().BeSameAs(worldList);
        response.Positive.Should().BeSameAs(positiveList);
    }
}

