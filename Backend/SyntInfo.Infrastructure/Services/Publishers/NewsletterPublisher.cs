using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Social;
using SyntInfo.Domain.Entities;
using SyntInfo.Infrastructure.Services.Newsletter;

namespace SyntInfo.Infrastructure.Services.Publishers;

public class NewsletterPublisher : ISocialPlatformPublisher
{
    private readonly IMailerLiteClient _mailerLiteClient;

    public NewsletterPublisher(IMailerLiteClient mailerLiteClient)
    {
        _mailerLiteClient = mailerLiteClient;
    }

    public SocialPlatformType PlatformType => SocialPlatformType.Newsletter;

    public async Task<PublishResult> PublishAsync(SocialPostDraft draft, NewsArticle article, CancellationToken cancellationToken = default)
    {
        try
        {
            var subject = article.Title;
            // Wspólny renderer gwarantuje wygląd 1:1 z kartą artykułu na portalu.
            var html = NewsletterHtmlRenderer.Render(new[] { NewsletterArticle.From(article, draft.Content) });
            var plainText = draft.Content;

            var campaignId = await _mailerLiteClient.SendNewsletterCampaignAsync(subject, html, plainText, cancellationToken);
            return PublishResult.Succeeded(campaignId);
        }
        catch (Exception ex)
        {
            return PublishResult.Failed(ex.Message);
        }
    }
}
