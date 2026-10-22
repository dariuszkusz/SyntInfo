using System.Text;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;

namespace SyntInfo.Infrastructure.Services;

public class SocialPostGenerator : ISocialPostGenerator
{
    private readonly IGoogleAiStudioClient _googleAiStudioClient;
    private readonly ILogger<SocialPostGenerator> _logger;

    public SocialPostGenerator(IGoogleAiStudioClient googleAiStudioClient, ILogger<SocialPostGenerator> logger)
    {
        _googleAiStudioClient = googleAiStudioClient;
        _logger = logger;
    }

    public async Task<string> GenerateContentForPlatformAsync(SocialPlatformType platform, NewsArticle article, CancellationToken cancellationToken = default)
    {
        var primaryUrl = article.SourceUrls.FirstOrDefault() ?? "https://syntinfo.local";
        var categoryTag = SanitizeTag(article.Category?.Name ?? "wiadomosci");

        try
        {
            var prompt = BuildPrompt(platform, article, primaryUrl, categoryTag);
            // Wykorzystujemy istniejącego klienta Gemini / OpenRouter z fallbackiem
            var factsContext = !string.IsNullOrWhiteSpace(article.DeepContent) ? article.DeepContent : article.SummaryText;
            var generatedText = await _googleAiStudioClient.GenerateSummaryFromFactsAsync(
                $"{factsContext}\n\n[POLECENIE SPECJALNE]: {prompt}", cancellationToken);

            if (!string.IsNullOrWhiteSpace(generatedText))
            {
                var cleaned = CleanOutput(generatedText);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    return cleaned;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się wygenerować treści LLM dla platformy {Platform}. Używam szablonu awaryjnego.", platform);
        }

        return GenerateFallbackTemplate(platform, article, primaryUrl, categoryTag);
    }

    public Task<string> GenerateDigestContentForPlatformAsync(SocialPlatformType platform, IReadOnlyList<NewsArticle> articles, CancellationToken cancellationToken = default)
    {
        if (articles == null || articles.Count == 0)
        {
            return Task.FromResult(string.Empty);
        }

        var result = GenerateStructuredDigest(platform, articles);
        return Task.FromResult(result);
    }

    private static string GenerateStructuredDigest(SocialPlatformType platform, IReadOnlyList<NewsArticle> articles)
    {
        var sb = new StringBuilder();
        var dateStr = DateTime.UtcNow.ToString("dd.MM.yyyy");

        switch (platform)
        {
            case SocialPlatformType.Hejto:
                sb.AppendLine($"#InfoSkrót — Wydanie z {dateStr}");
                sb.AppendLine();
                for (int i = 0; i < articles.Count; i++)
                {
                    var a = articles[i];
                    sb.AppendLine($"### {i + 1}. {a.Title}");
                    sb.AppendLine();

                    if (!string.IsNullOrWhiteSpace(a.ImageUrl))
                    {
                        sb.AppendLine($"![{a.Title}]({a.ImageUrl})");
                        sb.AppendLine();
                    }

                    sb.AppendLine(a.SummaryText);
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }

                sb.AppendLine("### Źródła:");
                for (int i = 0; i < articles.Count; i++)
                {
                    var a = articles[i];
                    var url = a.SourceUrls.FirstOrDefault() ?? "https://www.infoskrót.pl";
                    sb.AppendLine($"{i + 1}. [{a.Title}]({url})");
                }
                sb.AppendLine();
                sb.AppendLine("Więcej wiadomości i pełne archiwum: [www.infoskrót.pl](https://www.infoskrót.pl)");
                sb.AppendLine();
                sb.AppendLine("#wiadomosci #infoskrot #polska #swiat");
                break;

            case SocialPlatformType.Wykop:
                sb.AppendLine($"InfoSkrót — Wydanie z {dateStr}");
                sb.AppendLine();
                for (int i = 0; i < articles.Count; i++)
                {
                    var a = articles[i];
                    sb.AppendLine($"{i + 1}. {a.Title}");
                    sb.AppendLine(a.SummaryText);
                    sb.AppendLine();
                }

                sb.AppendLine("Źródła:");
                for (int i = 0; i < articles.Count; i++)
                {
                    var a = articles[i];
                    var url = a.SourceUrls.FirstOrDefault() ?? "https://www.infoskrót.pl";
                    sb.AppendLine($"{i + 1}. {a.Title}: {url}");
                }
                sb.AppendLine();
                sb.AppendLine("Więcej skrótów wiadomości znajdziesz na: https://www.infoskrót.pl");
                sb.AppendLine();
                sb.AppendLine("#wiadomosci #infoskrot #polska #swiat");
                break;

            case SocialPlatformType.Newsletter:
                sb.AppendLine("<!DOCTYPE html><html lang=\"pl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"><title>InfoSkrót</title></head>");
                sb.AppendLine("<body style=\"margin: 0; padding: 0; background-color: #E7F6F2; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #2C3333; -webkit-font-smoothing: antialiased;\">");
                sb.AppendLine("  <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"background-color: #E7F6F2; padding: 24px 12px;\">");
                sb.AppendLine("    <tr><td align=\"center\">");
                sb.AppendLine("      <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"max-width: 640px; margin: 0 auto;\">");
                
                // Header / Logo / Buy coffee
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

                // Info bar
                sb.AppendLine("        <tr><td style=\"padding-bottom: 16px;\">");
                sb.AppendLine("          <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\">");
                sb.AppendLine("            <tr>");
                sb.AppendLine($"              <td align=\"left\" style=\"font-size: 12px; font-weight: 600; color: #395B64;\">Wydanie codzienne &bull; {dateStr}</td>");
                sb.AppendLine("              <td align=\"right\" style=\"font-size: 12px; font-weight: 600; color: #0d9488;\">&#x23F1; 3 minuty czytania bez klikania</td>");
                sb.AppendLine("            </tr>");
                sb.AppendLine("          </table>");
                sb.AppendLine("        </td></tr>");

                // Article Cards
                for (int i = 0; i < articles.Count; i++)
                {
                    var a = articles[i];
                    var primaryUrl = a.SourceUrls.FirstOrDefault() ?? "https://infoskrot.pl";
                    var categoryName = !string.IsNullOrWhiteSpace(a.Category?.Name) ? a.Category.Name : "WIADOMOŚCI";
                    var regionTag = a.Region switch
                    {
                        Domain.Entities.SourceRegion.World => "&#x1F310; ŚWIAT",
                        Domain.Entities.SourceRegion.Positive => "&#x1F60A; POZYTYWNE",
                        _ => "&#x1F1F5;&#x1F1F1; POLSKA"
                    };
                    var domainHost = "infoskrot.pl";
                    try { if (Uri.TryCreate(primaryUrl, UriKind.Absolute, out var uri)) domainHost = uri.Host.Replace("www.", ""); } catch { }

                    sb.AppendLine("        <tr><td style=\"padding-bottom: 20px;\">");
                    sb.AppendLine("          <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"background-color: #ffffff; border-radius: 24px; border: 1px solid #d8ede7; box-shadow: 0 4px 16px rgba(44, 51, 51, 0.04); overflow: hidden;\">");
                    sb.AppendLine("            <tr><td style=\"padding: 24px;\">");
                    
                    // Card Meta
                    sb.AppendLine("              <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"margin-bottom: 14px;\">");
                    sb.AppendLine("                <tr>");
                    sb.AppendLine($"                  <td align=\"left\"><span style=\"display: inline-block; background-color: #E7F6F2; color: #2C3333; font-size: 10px; font-weight: 800; text-transform: uppercase; letter-spacing: 0.15em; padding: 4px 10px; border-radius: 9999px;\">{System.Net.WebUtility.HtmlEncode(categoryName)}</span></td>");
                    sb.AppendLine($"                  <td align=\"right\" style=\"font-size: 12px; font-weight: 600; color: #64748b;\"><span style=\"background-color: #E7F6F2; color: #2C3333; padding: 3px 8px; border-radius: 6px; font-size: 11px; margin-right: 6px;\">{regionTag}</span> <span style=\"opacity: 0.7;\">{a.PublishedAt:dd.MM, HH:mm}</span></td>");
                    sb.AppendLine("                </tr>");
                    sb.AppendLine("              </table>");

                    // Title
                    sb.AppendLine($"              <h2 style=\"margin: 0 0 14px 0; font-size: 21px; font-weight: 800; line-height: 1.35; color: #2C3333;\">{System.Net.WebUtility.HtmlEncode(a.Title)}</h2>");

                    // Body: Text + Image layout
                    sb.AppendLine("              <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"margin-bottom: 18px;\">");
                    sb.AppendLine("                <tr>");
                    sb.AppendLine($"                  <td valign=\"top\" style=\"font-size: 15px; line-height: 1.65; color: #395B64; text-align: justify; padding-right: {(!string.IsNullOrWhiteSpace(a.ImageUrl) ? "16px" : "0")};\">{System.Net.WebUtility.HtmlEncode(a.SummaryText)}</td>");
                    if (!string.IsNullOrWhiteSpace(a.ImageUrl))
                    {
                        sb.AppendLine($"                  <td valign=\"top\" width=\"160\" style=\"width: 160px; min-width: 140px;\"><img src=\"{System.Net.WebUtility.HtmlEncode(a.ImageUrl)}\" alt=\"{System.Net.WebUtility.HtmlEncode(a.Title)}\" style=\"width: 100%; height: 115px; object-fit: cover; border-radius: 16px; display: block; border: 1px solid rgba(0,0,0,0.06);\" /></td>");
                    }
                    sb.AppendLine("                </tr>");
                    sb.AppendLine("              </table>");

                    // Card Footer: Source & Essence
                    sb.AppendLine("              <table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"border-top: 1px solid #f1f5f9; padding-top: 14px;\">");
                    sb.AppendLine("                <tr>");
                    sb.AppendLine("                  <td align=\"left\" valign=\"middle\">");
                    sb.AppendLine("                    <table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\">");
                    sb.AppendLine("                      <tr>");
                    sb.AppendLine($"                        <td style=\"padding-right: 8px;\"><a href=\"{System.Net.WebUtility.HtmlEncode(primaryUrl)}\" target=\"_blank\" style=\"text-decoration: none; display: inline-block;\"><img src=\"https://www.google.com/s2/favicons?domain={domainHost}&sz=64\" width=\"20\" height=\"20\" style=\"border-radius: 6px; display: block;\" alt=\"icon\" /></a></td>");
                    sb.AppendLine($"                        <td style=\"font-size: 11px; color: #64748b;\">Źródło pierwotne &bull; <a href=\"{System.Net.WebUtility.HtmlEncode(primaryUrl)}\" target=\"_blank\" style=\"color: #0d9488; text-decoration: none; font-weight: 600;\">{domainHost}</a></td>");
                    sb.AppendLine("                      </tr>");
                    sb.AppendLine("                    </table>");
                    sb.AppendLine("                  </td>");
                    sb.AppendLine("                  <td align=\"right\" valign=\"middle\"><span style=\"font-size: 9px; font-weight: 800; text-transform: uppercase; letter-spacing: 0.2em; color: #94a3b8; font-style: italic;\">AI SUMMARIZED ESSENCE</span></td>");
                    sb.AppendLine("                </tr>");
                    sb.AppendLine("              </table>");

                    sb.AppendLine("            </td></tr>");
                    sb.AppendLine("          </table>");
                    sb.AppendLine("        </td></tr>");
                }

                // Global Footer
                sb.AppendLine("        <tr><td align=\"center\" style=\"padding: 24px 12px 12px; font-size: 12px; color: #64748b; line-height: 1.6;\">");
                sb.AppendLine("          <p style=\"margin: 0 0 8px 0; font-weight: 600; color: #395B64;\">InfoSkrót &bull; Skrót najważniejszych informacji bez klikania i clickbaitu.</p>");
                sb.AppendLine("          <p style=\"margin: 0 0 12px 0;\">Otrzymujesz tę wiadomość, ponieważ subskrybujesz newsletter na portalu <a href=\"https://infoskrot.pl\" style=\"color: #0d9488; text-decoration: none; font-weight: 700;\">infoskrot.pl</a>.</p>");
                sb.AppendLine("          <p style=\"margin: 0; font-size: 11px;\"><a href=\"{$unsubscribe}\" style=\"color: #64748b; text-decoration: underline;\">Wypisz się z newslettera</a></p>");
                sb.AppendLine("        </td></tr>");

                sb.AppendLine("      </table>");
                sb.AppendLine("    </td></tr>");
                sb.AppendLine("  </table>");
                sb.AppendLine("</body></html>");
                break;

            case SocialPlatformType.Facebook:
            default:
                sb.AppendLine($"InfoSkrót — Przegląd wiadomości z {dateStr}");
                sb.AppendLine();
                for (int i = 0; i < articles.Count; i++)
                {
                    var a = articles[i];
                    sb.AppendLine($"{i + 1}. {a.Title}");
                    sb.AppendLine(a.SummaryText);
                    sb.AppendLine();
                }
                sb.AppendLine("Źródła:");
                for (int i = 0; i < articles.Count; i++)
                {
                    var a = articles[i];
                    var url = a.SourceUrls.FirstOrDefault() ?? "https://www.infoskrót.pl";
                    sb.AppendLine($"{i + 1}. {url}");
                }
                sb.AppendLine();
                sb.AppendLine("Więcej na: https://www.infoskrót.pl");
                sb.AppendLine("#infoskrot #wiadomosci");
                break;
        }

        return sb.ToString();
    }


    private static string BuildPrompt(SocialPlatformType platform, NewsArticle article, string primaryUrl, string categoryTag)
    {
        return platform switch
        {
            SocialPlatformType.Wykop =>
                $"Napisz zwięzły wpis na mikroblog Wykop.pl (maksymalnie 400 znaków) na podstawie poniższych informacji. Tytuł: {article.Title}. " +
                $"Dołącz 2-3 kluczowe fakty, link do źródła: {primaryUrl} oraz odpowiednie hashtagi: #wiadomosci #{categoryTag} #infoskrot. Zwróć tylko czystą treść wpisu.",

            SocialPlatformType.Hejto =>
                $"Napisz angażujący wpis na portal Hejto.pl (maksymalnie 500 znaków) z formatowaniem Markdown na podstawie: {article.Title}. " +
                $"Przedstaw esencję newsa, link: {primaryUrl} i tagi: #wiadomosci #{categoryTag} #infoskrot #polska. Zwróć tylko treść.",

            SocialPlatformType.Facebook =>
                $"Napisz profesjonalny, angażujący post na Fanpage Facebook (około 400-600 znaków) z emotikonami o: {article.Title}. " +
                $"Przedstaw zwięzłe podsumowanie, zachęć krótko do dyskusji w komentarzu i wskaż link: {primaryUrl}. Zwróć tylko treść posta.",

            SocialPlatformType.Newsletter =>
                $"Napisz treść wydania newslettera (bullet pointy faktów, wstęp i podsumowanie) na temat: {article.Title}. " +
                $"Używaj przejrzystego języka, bez żargonu. Zwróć tylko gotową treść tekstu.",

            _ => $"Podsumuj news: {article.Title}"
        };
    }

    private static string CleanOutput(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```") && cleaned.EndsWith("```"))
        {
            var firstLineBreak = cleaned.IndexOf('\n');
            var lastCode = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineBreak >= 0 && lastCode > firstLineBreak)
            {
                cleaned = cleaned.Substring(firstLineBreak + 1, lastCode - firstLineBreak - 1).Trim();
            }
        }

        // Jeśli model zwrócił JSON z polem "essence" lub "content"
        if (cleaned.StartsWith("{") && cleaned.EndsWith("}"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(cleaned);
                if (doc.RootElement.TryGetProperty("essence", out var essEl))
                    return essEl.GetString() ?? cleaned;
                if (doc.RootElement.TryGetProperty("content", out var contEl))
                    return contEl.GetString() ?? cleaned;
            }
            catch
            {
                // ignoruj
            }
        }

        return cleaned;
    }

    private static string GenerateFallbackTemplate(SocialPlatformType platform, NewsArticle article, string primaryUrl, string categoryTag)
    {
        var sb = new StringBuilder();

        switch (platform)
        {
            case SocialPlatformType.Wykop:
                sb.AppendLine(article.Title);
                sb.AppendLine();
                sb.AppendLine(Truncate(article.SummaryText, 300));
                sb.AppendLine();
                sb.AppendLine($"Więcej: {primaryUrl}");
                sb.AppendLine($"#wiadomosci #{categoryTag} #infoskrot #polska");
                break;

            case SocialPlatformType.Hejto:
                sb.AppendLine($"### {article.Title}");
                sb.AppendLine();
                sb.AppendLine(article.SummaryText);
                sb.AppendLine();
                sb.AppendLine($"[Źródło artykułu]({primaryUrl})");
                sb.AppendLine();
                sb.AppendLine($"#wiadomosci #{categoryTag} #infoskrot");
                break;

            case SocialPlatformType.Facebook:
                sb.AppendLine(article.Title);
                sb.AppendLine();
                sb.AppendLine(article.SummaryText);
                sb.AppendLine();
                sb.AppendLine($"Czytaj więcej: {primaryUrl}");
                break;

            case SocialPlatformType.Newsletter:
                sb.AppendLine(article.Title);
                sb.AppendLine(new string('=', article.Title.Length));
                sb.AppendLine();
                sb.AppendLine(article.SummaryText);
                sb.AppendLine();
                sb.AppendLine($"Pełny materiał źródłowy: {primaryUrl}");
                break;
        }

        return sb.ToString();
    }

    private static string SanitizeTag(string text)
    {
        var clean = new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(clean) ? "wiadomosci" : clean;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}

