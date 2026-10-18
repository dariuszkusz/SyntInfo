using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Social;
using SyntInfo.Domain.Entities;

namespace SyntInfo.Infrastructure.Services.Publishers;

public class WykopPublisher : ISocialPlatformPublisher
{
    private readonly IWykopClient _wykopClient;

    public WykopPublisher(IWykopClient wykopClient)
    {
        _wykopClient = wykopClient;
    }

    public SocialPlatformType PlatformType => SocialPlatformType.Wykop;

    public async Task<PublishResult> PublishAsync(SocialPostDraft draft, NewsArticle article, CancellationToken cancellationToken = default)
    {
        try
        {
            var postId = await _wykopClient.CreateEntryAsync(draft.Content, null, cancellationToken);
            return PublishResult.Succeeded(postId);
        }
        catch (Exception ex)
        {
            return PublishResult.Failed(ex.Message);
        }
    }
}
