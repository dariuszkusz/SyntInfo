using SyntInfo.Application.Models.Social;

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
            string? imageUuid = null;
            if (!string.IsNullOrWhiteSpace(article.ImageUrl))
            {
                imageUuid = await _hejtoClient.UploadImageAsync(article.ImageUrl, cancellationToken);
            }

            var postId = await _hejtoClient.CreatePostAsync(draft.Content, null, imageUuid, cancellationToken);
            return PublishResult.Succeeded(postId);
        }
        catch (Exception ex)
        {
            return PublishResult.Failed(ex.Message);
        }
    }
}
