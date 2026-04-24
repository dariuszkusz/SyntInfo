using Pgvector;

namespace SyntInfo.Domain.Entities;

public class NewsArticle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string OriginalTitle { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    
    // Storing source links, e.g. as a JSON array or related table. 
    // We can use a simple List<string> for PostgreSQL which supports text arrays natively
    public List<string> SourceUrls { get; set; } = new();

    public Vector? Embedding { get; set; }

    public Guid CategoryId { get; set; }
    public NewsCategory? Category { get; set; }
    
    public SourceRegion Region { get; set; }

    public bool IsActive { get; set; } = true;
    public string? DeepContent { get; set; }
}
