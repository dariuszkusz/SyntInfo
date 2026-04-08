using System;
using System.Collections.Generic;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.Application.CQRS.Queries
{
    public class NewsArticleDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SummaryText { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public List<string> SourceUrls { get; set; } = new();
        public string CategoryName { get; set; } = string.Empty;
    }

    public record GetNewsArticlesQuery(int Page = 1, int PageSize = 20);
}
