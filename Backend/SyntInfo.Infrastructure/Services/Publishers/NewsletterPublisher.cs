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
        var primaryUrl = article.SourceUrls.FirstOrDefault() ?? "https://infoskrot.pl";
        var dateStr = DateTime.UtcNow.ToString("dd.MM.yyyy");
        var domainHost = "infoskrot.pl";
        try { if (Uri.TryCreate(primaryUrl, UriKind.Absolute, out var uri)) domainHost = uri.Host.Replace("www.", ""); } catch { }

        var categoryName = !string.IsNullOrWhiteSpace(article.Category?.Name) ? article.Category.Name : "WIADOMOŚCI";
        var regionTag = article.Region switch
        {
            Domain.Entities.SourceRegion.World => "&#x1F310; ŚWIAT",
            Domain.Entities.SourceRegion.Positive => "&#x1F60A; POZYTYWNE",
            _ => "&#x1F1F5;&#x1F1F1; POLSKA"
        };

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"pl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"><title>InfoSkrót</title></head>");
        sb.AppendLine("<body style=\"margin: 0; padding: 0; background-color: #E7F6F2; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #2C3333; -webkit-font-smoothing: antialiased;\">");
        sb.AppendLine("  <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"background-color: #E7F6F2; padding: 24px 12px;\">");
        sb.AppendLine("    <tr><td align=\"center\">");
        sb.AppendLine("      <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"max-width: 640px; margin: 0 auto;\">");
        
        // Header
        sb.AppendLine("        <tr><td style=\"padding-bottom: 24px;\">");
        sb.AppendLine("          <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\">");
        sb.AppendLine("            <tr>");
        sb.AppendLine("              <td valign=\"middle\" align=\"left\">");
        sb.AppendLine("                <table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\">");
        sb.AppendLine("                  <tr>");
        sb.AppendLine("                    <td style=\"vertical-align: middle; padding-right: 12px;\">");
        sb.AppendLine("                      <div style=\"background-color: #0d9488; color: #ffffff; font-weight: 800; font-size: 15px; width: 36px; height: 36px; line-height: 36px; text-align: center; border-radius: 10px; letter-spacing: -0.5px;\">IS</div>");
        sb.AppendLine("                    </td>");
        sb.AppendLine("                    <td style=\"vertical-align: middle;\">");
        sb.AppendLine("                      <div style=\"font-size: 24px; font-weight: 800; line-height: 1; color: #2C3333; letter-spacing: -0.5px;\">Info<span style=\"color: #0d9488; font-style: italic;\">Skrót</span></div>");
        sb.AppendLine("                      <div style=\"font-size: 9px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.18em; color: #64748b; margin-top: 4px;\">SKRÓT NAJWAŻNIEJSZYCH INFORMACJI</div>");
        sb.AppendLine("                    </td>");
        sb.AppendLine("                  </tr>");
        sb.AppendLine("                </table>");
        sb.AppendLine("              </td>");
        sb.AppendLine("              <td valign=\"middle\" align=\"right\">");
        sb.AppendLine("                <a href=\"https://buycoffee.to/infoskrot\" target=\"_blank\" style=\"display: inline-block; background-color: #f59e0b; color: #2C3333; text-decoration: none; font-size: 12px; font-weight: 700; padding: 8px 14px; border-radius: 9999px; box-shadow: 0 1px 2px rgba(0,0,0,0.05);\">&#x2615; Postaw kawę</a>");
        sb.AppendLine("              </td>");
        sb.AppendLine("            </tr>");
        sb.AppendLine("          </table>");
        sb.AppendLine("        </td></tr>");

        // Card
        sb.AppendLine("        <tr><td style=\"padding-bottom: 20px;\">");
        sb.AppendLine("          <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"background-color: #ffffff; border-radius: 24px; border: 1px solid #d8ede7; box-shadow: 0 4px 16px rgba(44, 51, 51, 0.04); overflow: hidden;\">");
        sb.AppendLine("            <tr><td style=\"padding: 24px;\">");
        
        sb.AppendLine("              <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"margin-bottom: 14px;\">");
        sb.AppendLine("                <tr>");
        sb.AppendLine($"                  <td align=\"left\"><span style=\"display: inline-block; background-color: #E7F6F2; color: #2C3333; font-size: 10px; font-weight: 800; text-transform: uppercase; letter-spacing: 0.15em; padding: 4px 10px; border-radius: 9999px;\">{WebUtility.HtmlEncode(categoryName)}</span></td>");
        sb.AppendLine($"                  <td align=\"right\" style=\"font-size: 12px; font-weight: 600; color: #64748b;\"><span style=\"background-color: #E7F6F2; color: #2C3333; padding: 3px 8px; border-radius: 6px; font-size: 11px; margin-right: 6px;\">{regionTag}</span> <span style=\"opacity: 0.7;\">{article.PublishedAt:dd.MM, HH:mm}</span></td>");
        sb.AppendLine("                </tr>");
        sb.AppendLine("              </table>");

        sb.AppendLine($"              <h2 style=\"margin: 0 0 14px 0; font-size: 21px; font-weight: 800; line-height: 1.35; color: #2C3333;\">{WebUtility.HtmlEncode(article.Title)}</h2>");

        sb.AppendLine("              <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"margin-bottom: 18px;\">");
        sb.AppendLine("                <tr>");
        sb.AppendLine($"                  <td valign=\"top\" style=\"font-size: 15px; line-height: 1.65; color: #395B64; text-align: justify; padding-right: {(!string.IsNullOrWhiteSpace(article.ImageUrl) ? "16px" : "0")};\">{WebUtility.HtmlEncode(draftContent)}</td>");
        if (!string.IsNullOrWhiteSpace(article.ImageUrl))
        {
            sb.AppendLine($"                  <td valign=\"top\" width=\"160\" style=\"width: 160px; min-width: 140px;\"><img src=\"{WebUtility.HtmlEncode(article.ImageUrl)}\" alt=\"{WebUtility.HtmlEncode(article.Title)}\" style=\"width: 100%; height: 115px; object-fit: cover; border-radius: 16px; display: block; border: 1px solid rgba(0,0,0,0.06);\" /></td>");
        }
        sb.AppendLine("                </tr>");
        sb.AppendLine("              </table>");

        sb.AppendLine("              <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"border-top: 1px solid #f1f5f9; padding-top: 14px;\">");
        sb.AppendLine("                <tr>");
        sb.AppendLine("                  <td align=\"left\" valign=\"middle\">");
        sb.AppendLine("                    <table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\">");
        sb.AppendLine("                      <tr>");
        sb.AppendLine($"                        <td style=\"padding-right: 8px;\"><a href=\"{WebUtility.HtmlEncode(primaryUrl)}\" target=\"_blank\" style=\"text-decoration: none; display: inline-block;\"><img src=\"https://www.google.com/s2/favicons?domain={domainHost}&sz=64\" width=\"20\" height=\"20\" style=\"border-radius: 6px; display: block;\" alt=\"icon\" /></a></td>");
        sb.AppendLine($"                        <td style=\"font-size: 11px; color: #64748b;\">Źródło pierwotne &bull; <a href=\"{WebUtility.HtmlEncode(primaryUrl)}\" target=\"_blank\" style=\"color: #0d9488; text-decoration: none; font-weight: 600;\">{domainHost}</a></td>");
        sb.AppendLine("                      </tr>");
        sb.AppendLine("                    </table>");
        sb.AppendLine("                  </td>");
        sb.AppendLine("                  <td align=\"right\" valign=\"middle\"><span style=\"font-size: 9px; font-weight: 800; text-transform: uppercase; letter-spacing: 0.2em; color: #94a3b8; font-style: italic;\">AI SUMMARIZED ESSENCE</span></td>");
        sb.AppendLine("                </tr>");
        sb.AppendLine("              </table>");

        sb.AppendLine("            </td></tr>");
        sb.AppendLine("          </table>");
        sb.AppendLine("        </td></tr>");

        // Footer
        sb.AppendLine("        <tr><td align=\"center\" style=\"padding: 24px 12px 12px; font-size: 12px; color: #64748b; line-height: 1.6;\">");
        sb.AppendLine("          <p style=\"margin: 0 0 8px 0; font-weight: 600; color: #395B64;\">InfoSkrót &bull; Skrót najważniejszych informacji bez klikania i clickbaitu.</p>");
        sb.AppendLine("          <p style=\"margin: 0 0 12px 0;\">Otrzymujesz tę wiadomość, ponieważ subskrybujesz newsletter na portalu <a href=\"https://infoskrot.pl\" style=\"color: #0d9488; text-decoration: none; font-weight: 700;\">infoskrot.pl</a>.</p>");
        sb.AppendLine("          <p style=\"margin: 0; font-size: 11px;\"><a href=\"{$unsubscribe}\" style=\"color: #64748b; text-decoration: underline;\">Wypisz się z newslettera</a></p>");
        sb.AppendLine("        </td></tr>");

        sb.AppendLine("      </table>");
        sb.AppendLine("    </td></tr>");
        sb.AppendLine("  </table>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }
}
