using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Application.DTOs;

namespace SyntInfo.Application.CQRS.Handlers;

public class GetNewsArticlesQueryHandler
{
    private readonly IUnitOfWork _uow;

    public GetNewsArticlesQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<List<NewsArticleDto>> Handle(GetNewsArticlesQuery query, CancellationToken cancellationToken = default)
    {
        var articles = await _uow.NewsArticles.GetRecentArticlesAsync(query.Region, query.Page, query.PageSize, cancellationToken);

        return articles.Select(a => new NewsArticleDto
        {
            Id = a.Id,
            Title = a.Title.Trim(),
            OriginalTitle = a.OriginalTitle.Trim(),
            SummaryText = a.SummaryText.Trim(),
            PublishedAt = a.PublishedAt,
            SourceUrls = a.SourceUrls,
            Tags = a.Tags,
            CategoryName = a.Category != null ? a.Category.Name : "General",
            Region = a.Region,
            ImageUrl = a.ImageUrl
        }).ToList();
    }
}
