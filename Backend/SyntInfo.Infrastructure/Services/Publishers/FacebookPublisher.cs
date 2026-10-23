using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Social;
using SyntInfo.Domain.Entities;

namespace SyntInfo.Infrastructure.Services.Publishers;

public class FacebookPublisher : ISocialPlatformPublisher
{
    private readonly IFacebookClient _facebookClient;

    public FacebookPublisher(IFacebookClient facebookClient)
    {
        _facebookClient = facebookClient;
    }

    public SocialPlatformType PlatformType => SocialPlatformType.Facebook;

    public async Task<PublishResult> PublishAsync(SocialPostDraft draft, NewsArticle article, CancellationToken cancellationToken = default)
    {
        try
        {
            var primaryLink = article.SourceUrls.FirstOrDefault();
            var postId = await _facebookClient.PublishPagePostAsync(draft.Content, primaryLink, cancellationToken);
            return PublishResult.Succeeded(postId);
        }
        catch (Exception ex)
        {
            return PublishResult.Failed(ex.Message);
        }
    }
}
