namespace SyntInfo.Domain.Entities;

public class NewsSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string RssUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime LastFetchedAt { get; set; }
}
