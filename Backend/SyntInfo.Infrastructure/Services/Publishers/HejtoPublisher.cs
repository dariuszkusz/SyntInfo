using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Social;
using SyntInfo.Domain.Entities;

namespace SyntInfo.Infrastructure.Services.Publishers;

public class HejtoPublisher : ISocialPlatformPublisher
{
    private readonly IHejtoClient _hejtoClient;

    public HejtoPublisher(IHejtoClient hejtoClient)
    {
        _hejtoClient = hejtoClient;
    }

    public SocialPlatformType PlatformType => SocialPlatformType.Hejto;

    public async Task<PublishResult> PublishAsync(SocialPostDraft draft, NewsArticle article, CancellationToken cancellationToken = default)
    {
        try
        {
            var postId = await _hejtoClient.CreatePostAsync(draft.Content, null, cancellationToken);
            return PublishResult.Succeeded(postId);
        }
        catch (Exception ex)
        {
            return PublishResult.Failed(ex.Message);
        }
    }
}
