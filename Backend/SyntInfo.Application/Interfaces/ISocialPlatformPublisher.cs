using SyntInfo.Application.Models.Social;

namespace SyntInfo.Application.Interfaces;

public interface ISocialPlatformPublisher
{
    SocialPlatformType PlatformType { get; }

    Task<PublishResult> PublishAsync(SocialPostDraft draft, NewsArticle article, CancellationToken cancellationToken = default);
}
