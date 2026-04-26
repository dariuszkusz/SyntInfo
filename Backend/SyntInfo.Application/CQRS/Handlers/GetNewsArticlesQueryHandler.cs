using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Application.DTOs;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class GetNewsArticlesQueryHandler
    {
        private readonly IUnitOfWork _uow;

        public GetNewsArticlesQueryHandler(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public async Task<List<NewsArticleDto>> Handle(GetNewsArticlesQuery query, CancellationToken cancellationToken = default)
        {
            var cutoffDate = DateTime.UtcNow.AddHours(-24);
            var dbQuery = _uow.Repository<NewsArticle>().Query()
                .Where(a => a.IsActive && a.PublishedAt >= cutoffDate);

            if (query.Region.HasValue)
            {
                dbQuery = dbQuery.Where(a => a.Region == query.Region.Value);
            }

            var articles = await dbQuery
                .Include(a => a.Category)
                .OrderByDescending(a => a.PublishedAt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(a => new NewsArticleDto
                {
                    Id = a.Id,
                    Title = a.Title,
                    OriginalTitle = a.OriginalTitle,
                    SummaryText = a.SummaryText,
                    PublishedAt = a.PublishedAt,
                    SourceUrls = a.SourceUrls,
                    CategoryName = a.Category != null ? a.Category.Name : "General",
                    Region = a.Region
                })
                .ToListAsync(cancellationToken);

            return articles;
        }
    }
}

