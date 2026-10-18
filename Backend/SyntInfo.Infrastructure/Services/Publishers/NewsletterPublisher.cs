using System.Net;
using System.Text;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Social;
using SyntInfo.Domain.Entities;

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
            var html = GenerateEmailHtml(article, draft.Content);
            var plainText = draft.Content;

            var campaignId = await _mailerLiteClient.SendNewsletterCampaignAsync(subject, html, plainText, cancellationToken);
            return PublishResult.Succeeded(campaignId);
        }
        catch (Exception ex)
        {
            return PublishResult.Failed(ex.Message);
        }
    }

    private static string GenerateEmailHtml(NewsArticle article, string draftContent)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'/><style>");
        sb.Append("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #0f172a; color: #f8fafc; padding: 24px; }");
        sb.Append(".card { max-width: 600px; margin: 0 auto; background-color: #1e293b; border-radius: 12px; padding: 24px; box-shadow: 0 4px 6px rgba(0,0,0,0.3); }");
        sb.Append("h1 { color: #38bdf8; font-size: 20px; line-height: 1.4; margin-top: 0; }");
        sb.Append(".content { color: #e2e8f0; font-size: 15px; line-height: 1.6; white-space: pre-wrap; margin: 16px 0; }");
        sb.Append(".sources { margin-top: 20px; padding-top: 16px; border-top: 1px solid #334155; font-size: 13px; color: #94a3b8; }");
        sb.Append("a { color: #38bdf8; text-decoration: none; }");
        sb.Append("</style></head><body>");
        sb.Append("<div class='card'>");
        sb.Append($"<h1>{WebUtility.HtmlEncode(article.Title)}</h1>");
        sb.Append($"<div class='content'>{WebUtility.HtmlEncode(draftContent)}</div>");

        if (article.SourceUrls.Any())
        {
            sb.Append("<div class='sources'><strong>Źródła:</strong><ul>");
            foreach (var url in article.SourceUrls.Take(3))
            {
                sb.Append($"<li><a href='{WebUtility.HtmlEncode(url)}' target='_blank'>{WebUtility.HtmlEncode(url)}</a></li>");
            }
            sb.Append("</ul></div>");
        }

        sb.Append("<div style='margin-top: 24px; font-size: 12px; color: #64748b; text-align: center;'>");
        sb.Append("SyntInfo &bull; Półautomatyczny agregator wiadomości");
        sb.Append("<div style='margin-top: 8px;'><a href='{$unsubscribe}' style='color: #94a3b8; text-decoration: underline; font-size: 11px;'>Wypisz się z newslettera</a></div>");
        sb.Append("</div>");
        sb.Append("</div></body></html>");

        return sb.ToString();
    }
}
