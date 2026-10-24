namespace SyntInfo.Application.Interfaces;

public class DiscoveredNewsItem
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public SourceRegion Region { get; set; }
}

public interface INewsDiscoveryService
{
    Task<List<DiscoveredNewsItem>> DiscoverNewsAsync(SourceRegion region, CancellationToken cancellationToken = default);
}
