using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Application.DTOs;
using SyntInfo.Domain.Entities;
using Wolverine;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class GetTopNewsQueryHandler
    {
        private readonly IMessageBus _bus;

        public GetTopNewsQueryHandler(IMessageBus bus)
        {
            _bus = bus;
        }

        public async Task<TopNewsResponse> Handle(GetTopNewsQuery query, CancellationToken cancellationToken)
        {
            var polandQuery = new GetNewsArticlesQuery(1, 10, SourceRegion.Poland);
            var worldQuery = new GetNewsArticlesQuery(1, 10, SourceRegion.World);

            var polandNews = await _bus.InvokeAsync<List<NewsArticleDto>>(polandQuery, cancellationToken);
            var worldNews = await _bus.InvokeAsync<List<NewsArticleDto>>(worldQuery, cancellationToken);

            return new TopNewsResponse
            {
                Poland = polandNews,
                World = worldNews
            };
        }
    }
}
