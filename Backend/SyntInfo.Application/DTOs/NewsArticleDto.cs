using SyntInfo.Domain.Entities;

namespace SyntInfo.Application.DTOs
{
    public class NewsArticleDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string OriginalTitle { get; set; } = string.Empty;
        public string SummaryText { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public List<string> SourceUrls { get; set; } = new();
        public string CategoryName { get; set; } = string.Empty;
        public SourceRegion Region { get; set; }
    }
}
