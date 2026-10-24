using CodeHollow.FeedReader;

namespace SyntInfo.Application.Interfaces;

public interface IRssFeedClient
{
    Task<Feed?> FetchFeedAsync(string url, CancellationToken cancellationToken = default);
}
