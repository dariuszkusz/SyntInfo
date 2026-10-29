using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Application.DTOs;
using Wolverine;

namespace SyntInfo.Application.CQRS.Handlers;

public class GetTopNewsQueryHandler
{
    private readonly IMessageBus _bus;
    private readonly ProcessingOptions _options;

    public GetTopNewsQueryHandler(IMessageBus bus, IOptions<ProcessingOptions> options)
    {
        _bus = bus;
        _options = options.Value;
    }

    public async Task<TopNewsResponse> Handle(GetTopNewsQuery query, CancellationToken cancellationToken)
    {
        var polandQuery = new GetNewsArticlesQuery(1, _options.MaxArticlesPerRegion, SourceRegion.Poland);
        var worldQuery = new GetNewsArticlesQuery(1, _options.MaxArticlesPerRegion, SourceRegion.World);
        var positiveQuery = new GetNewsArticlesQuery(1, _options.MaxPositiveArticles, SourceRegion.Positive);

        var polandNews = await _bus.InvokeAsync<List<NewsArticleDto>>(polandQuery, cancellationToken);
        var worldNews = await _bus.InvokeAsync<List<NewsArticleDto>>(worldQuery, cancellationToken);
        var positiveNews = await _bus.InvokeAsync<List<NewsArticleDto>>(positiveQuery, cancellationToken);

        return new TopNewsResponse
        {
            Poland = polandNews,
            World = worldNews,
            Positive = positiveNews
        };
    }
}
