using SyntInfo.Domain.Entities;

namespace SyntInfo.Application.CQRS.Queries
{
    public record GetNewsArticlesQuery(
        int Page = 1,
        int PageSize = 20,
        SourceRegion? Region = null);
}
