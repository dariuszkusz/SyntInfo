using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class GetNewsArticlesQueryHandler : IQueryHandler<GetNewsArticlesQuery, List<NewsArticleDto>>
    {
        private readonly IUnitOfWork _uow;

        public GetNewsArticlesQueryHandler(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public async Task<List<NewsArticleDto>> HandleAsync(GetNewsArticlesQuery query, CancellationToken cancellationToken = default)
        {
            var articles = await _uow.Repository<NewsArticle>().Query()
                .Include(a => a.Category)
                .OrderByDescending(a => a.PublishedAt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(a => new NewsArticleDto
                {
                    Id = a.Id,
                    Title = a.Title,
                    SummaryText = a.SummaryText,
                    PublishedAt = a.PublishedAt,
                    SourceUrls = a.SourceUrls,
                    CategoryName = a.Category != null ? a.Category.Name : "General"
                })
                .ToListAsync(cancellationToken);

            return articles;
        }
    }
}
