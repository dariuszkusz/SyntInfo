using System.Globalization;
using System.Net;
using System.Text;
using SyntInfo.Domain.Entities;

namespace SyntInfo.Infrastructure.Services.Newsletter;

/// <summary>Widok artykułu na potrzeby renderowania newslettera (niezależny od EF).</summary>
public sealed record NewsletterArticle(
    string Title,
    string SummaryText,
    string CategoryName,
    SourceRegion Region,
    DateTime PublishedAtUtc,
    string? ImageUrl,
    IReadOnlyList<string> SourceUrls)
{
    public static NewsletterArticle From(NewsArticle article, string? summaryOverride = null) => new(
        article.Title,
        summaryOverride ?? article.SummaryText,
        article.Category?.Name ?? "Wiadomości",
        article.Region,
        article.PublishedAt,
        article.ImageUrl,
        article.SourceUrls);
}

/// <summary>
/// Renderuje HTML newslettera 1:1 z wyglądem portalu (frontend/src/app/features/news-feed/news-feed.component.ts).
/// Tokeny kolorów i typografii odpowiadają klasom Tailwind użytym na stronie.
/// </summary>
public static class NewsletterHtmlRenderer
{
    // --- Design tokens (news-feed.component.ts) ---
    private const string PageBg = "#e9edeb";        // .bg-aux1 (override)
    private const string TextAux2 = "#2d3436";      // .text-aux2
    private const string TextAux3 = "#1e272e";      // .text-aux3
    private const string Primary = "#00b894";       // .text-primary
    private const string PillBg = "#edf0ef";        // bg-aux1/80 na białym
    private const string BadgeBg = "#f0f2f1";       // bg-aux1/70 na białym
    private const string FooterIconBg = "#f8f9f9";  // bg-aux1/30 na białym
    private const string CardBorder = "#f4f6f5";    // border-aux1/50
    private const string FontStack = "ui-sans-serif, system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif, 'Apple Color Emoji', 'Segoe UI Emoji'";

    private const string CoffeeUrl = "https://buymeacoffee.com/dariuszkusz";

    public static string Render(IReadOnlyList<NewsletterArticle> articles, string siteUrl = "https://infoskrot.pl")
    {
        siteUrl = siteUrl.TrimEnd('/');
        var sb = new StringBuilder();

        sb.Append($@"
<!DOCTYPE html>
<html lang=""pl"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<meta name=""color-scheme"" content=""light only"">
<title>InfoSkrót – Skrót najważniejszych informacji</title>
<style>
  body {{ margin:0; padding:0; }}
  @media only screen and (max-width: 620px) {{
    .is-card-pad {{ padding: 24px !important; }}
    .is-col-text, .is-col-img {{ display:block !important; width:100% !important; padding:0 !important; }}
    .is-col-img {{ padding-bottom: 20px !important; }}
    .is-title {{ font-size: 20px !important; }}
    .is-coffee-label {{ display:none !important; }}
  }}
</style>
</head>
<body style=""margin:0; padding:0; background-color:{PageBg}; font-family:{FontStack}; color:{TextAux2}; -webkit-font-smoothing:antialiased;"">
<table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color:{PageBg};"">
  <tr><td align=""center"" style=""padding:0;"">

    <!-- Header: bg-white/80 border-b border-primary/20 p-4 -->
    <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color:#ffffff; border-bottom:1px solid #ccf1ea;"">
      <tr><td align=""center"" style=""padding:16px;"">
        <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""max-width:896px;"">
          <tr>
            <td align=""left"" valign=""middle"">
              <a href=""{siteUrl}"" target=""_blank"" style=""text-decoration:none;"">
              <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0""><tr>
                <td valign=""middle"" style=""padding-right:12px;"">
                  <img src=""{siteUrl}/icons/icon-128x128.png"" width=""32"" height=""32"" alt=""InfoSkrót"" style=""display:block; width:32px; height:32px; border-radius:4px; border:0;"">
                </td>
                <td valign=""middle"">
                  <div style=""font-family:{FontStack}; font-size:24px; line-height:24px; font-weight:700; letter-spacing:-0.025em; color:{TextAux3};"">Info<span style=""color:{Primary}; font-style:italic;"">Skrót</span></div>
                  <div style=""font-family:{FontStack}; font-size:10px; line-height:15px; font-weight:600; letter-spacing:0.1em; text-transform:uppercase; color:{TextAux2}; opacity:0.6; margin-top:4px;"">Skrót najważniejszych informacji</div>
                </td>
              </tr></table>
              </a>
            </td>
            <td align=""right"" valign=""middle"">
              <a href=""{CoffeeUrl}"" target=""_blank"" style=""display:inline-block; font-family:{FontStack}; font-size:12px; line-height:16px; font-weight:700; color:#451a03; background-color:#fbbf24; border:1px solid #f5c451; border-radius:9999px; padding:6px 14px; text-decoration:none;"">&#9749;<span class=""is-coffee-label"">&nbsp; Postaw kawę</span></a>
            </td>
          </tr>
        </table>
      </td></tr>
    </table>

    <!-- Main: max-w-4xl p-4 py-8 -->
    <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""max-width:896px;"">
      <tr><td style=""padding:32px 16px 8px 16px;"">
        <div style=""font-family:{FontStack}; font-size:10px; font-weight:700; letter-spacing:0.05em; text-transform:uppercase; color:{TextAux2}; opacity:0.6;"">Wydanie z {FormatIssueDate(DateTime.UtcNow)} &bull; {articles.Count} {PluralArticles(articles.Count)}</div>
      </td></tr>
");

        foreach (var a in articles)
        {
            AppendCard(sb, a);
        }

        sb.Append($@"
    </table>

    <!-- Footer: bg-white border-t py-8 px-4 -->
    <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color:#ffffff; border-top:1px solid #f4f6f5; margin-top:24px;"">
      <tr><td align=""center"" style=""padding:32px 16px;"">
        <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""max-width:576px;"">
          <tr><td align=""center"" style=""padding-bottom:12px;"">
            <a href=""{CoffeeUrl}"" target=""_blank"" style=""display:inline-block; font-family:{FontStack}; font-size:12px; line-height:16px; font-weight:700; color:#451a03; background-color:#fbbf24; border:1px solid #f5c451; border-radius:9999px; padding:6px 14px; text-decoration:none;"">&#9749;&nbsp; Postaw kawę</a>
          </td></tr>
          <tr><td align=""center"" style=""font-family:{FontStack}; font-size:12px; line-height:16px; font-weight:700; color:{TextAux3}; padding-bottom:12px;"">
            Info Skrót - Twój codzienny skrót informacji i wiadomości w pigułce.
          </td></tr>
          <tr><td align=""center"" style=""font-family:{FontStack}; font-size:11px; line-height:18px; color:{TextAux2}; opacity:0.7; padding-bottom:12px;"">
            Otrzymujesz tę wiadomość, ponieważ zapisałeś się do newslettera na <a href=""{siteUrl}"" target=""_blank"" style=""color:{Primary}; font-weight:700; text-decoration:none;"">infoskrot.pl</a>.
          </td></tr>
          <tr><td align=""center"" style=""font-family:{FontStack}; font-size:10px; line-height:15px; color:{TextAux2}; opacity:0.5;"">
            &copy; {DateTime.UtcNow.Year} InfoSkrót &bull; <a href=""{{$unsubscribe}}"" style=""color:{TextAux2}; text-decoration:underline;"">Wypisz się z newslettera</a>
          </td></tr>
        </table>
      </td></tr>
    </table>

  </td></tr>
</table>
</body>
</html>
");

        return sb.ToString();
    }

    private static void AppendCard(StringBuilder sb, NewsletterArticle a)
    {
        // Warianty kart identyczne jak [ngClass] w news-feed.component.ts
        var (cardBg, cardBorder) = a.Region switch
        {
            SourceRegion.Positive => ("#f4fbf7", "#d8f3e5"),
            SourceRegion.World => ("#faf7f0", "#ebe4d8"),
            _ => ("#ffffff", CardBorder)
        };

        var secureImageUrl = SafeHttps(a.ImageUrl);
        var hasImage = !string.IsNullOrWhiteSpace(secureImageUrl);
        var textColStyle = hasImage ? "width:66%; padding-right:24px;" : string.Empty;

        sb.Append($@"
      <tr><td style=""padding:16px 16px 8px 16px;"">
        <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color:{cardBg}; border:1px solid {cardBorder}; border-radius:24px; box-shadow:0 1px 2px 0 rgba(0,0,0,0.05);"">
          <tr><td class=""is-card-pad"" style=""padding:32px;"">

            <!-- Card top meta -->
            <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""margin-bottom:20px;"">
              <tr>
                <td align=""left"" valign=""middle"">
                  <span style=""display:inline-block; font-family:{FontStack}; font-size:10px; line-height:15px; font-weight:700; letter-spacing:0.2em; text-transform:uppercase; color:{TextAux3}; background-color:{PillBg}; border-radius:9999px; padding:4px 12px;"">{Enc(a.CategoryName)}</span>
                </td>
                <td align=""right"" valign=""middle"" style=""font-family:{FontStack}; font-size:10px; line-height:15px; font-weight:700; letter-spacing:0.05em; text-transform:uppercase; white-space:nowrap;"">
                  {RegionBadge(a.Region)}<span style=""color:{TextAux2}; opacity:0.5; font-weight:500; padding-left:8px;"">{FormatShortDate(a.PublishedAtUtc)}</span>
                </td>
              </tr>
            </table>

            <!-- Body: 2/3 text + 1/3 image -->
            <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""margin-bottom:24px;"">
              <tr>
                <td class=""is-col-text"" valign=""top"" style=""{textColStyle}"">
                  <h2 class=""is-title"" style=""margin:0 0 16px 0; font-family:{FontStack}; font-size:24px; line-height:30px; font-weight:700; color:{TextAux3};"">{Enc(a.Title)}</h2>
                  <div style=""font-family:{FontStack}; font-size:15px; line-height:24px; letter-spacing:0.025em; text-align:justify; color:{TextAux2}; opacity:0.9;"">{Enc(a.SummaryText.Trim())}</div>
                </td>
");

        if (hasImage)
        {
            sb.Append($@"
                <td class=""is-col-img"" valign=""top"" style=""width:34%;"">
                  <img src=""{Enc(secureImageUrl!)}"" alt=""{Enc(a.Title)}"" width=""260"" style=""display:block; width:100%; max-width:100%; height:auto; aspect-ratio:4/3; object-fit:cover; border-radius:16px; border:1px solid rgba(0,0,0,0.05);"">
                </td>
");
        }

        sb.Append(@"
              </tr>
            </table>

            <!-- Footer: sources + essence tag -->
            <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""border-top:1px solid #f9faf9;"">
              <tr>
                <td align=""left"" valign=""middle"" style=""padding-top:20px;"">
                  <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0""><tr>
");

        foreach (var url in a.SourceUrls.Take(4))
        {
            var host = HostOf(url);
            sb.Append($@"
                    <td style=""padding-right:12px;"">
                      <a href=""{Enc(url)}"" target=""_blank"" title=""Otwórz źródło: {Enc(host)}"" style=""display:block; width:32px; height:32px; background-color:{FooterIconBg}; border-radius:12px; text-align:center; text-decoration:none;"">
                        <img src=""https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&amp;sz=64"" width=""16"" height=""16"" alt=""{Enc(host)}"" style=""display:inline-block; width:16px; height:16px; margin-top:8px; border:0;"">
                      </a>
                    </td>
");
        }

        sb.Append($@"
                  </tr></table>
                </td>
                <td align=""right"" valign=""middle"" style=""padding-top:20px; font-family:{FontStack}; font-size:9px; font-weight:700; font-style:italic; letter-spacing:0.1em; text-transform:uppercase; color:{TextAux2}; opacity:0.3;"">
                  AI Summarized Essence
                </td>
              </tr>
            </table>

          </td></tr>
        </table>
      </td></tr>
");

    }

    private static string RegionBadge(SourceRegion region) => region switch
    {
        // bg-emerald-100/70 text-emerald-800/90
        SourceRegion.Positive => "<span style=\"display:inline-block; background-color:#d8f8e9; color:#166a4e; border-radius:6px; padding:2px 8px;\">&#128578; Pozytywne</span>",
        // bg-amber-100/70 text-amber-900/80
        SourceRegion.World => $"<span style=\"display:inline-block; background-color:#fdf3d3; color:#8a4a1f; border-radius:6px; padding:2px 8px;\"><span style=\"color:{Primary};\">&#127760;</span> Świat</span>",
        // bg-aux1/70 text-aux3/80 + flaga PL (flag-icons)
        _ => $"<span style=\"display:inline-block; background-color:{BadgeBg}; color:#4b5258; border-radius:6px; padding:2px 8px;\">{PolishFlag()} Polska</span>"
    };

    // Flaga PL zbudowana z tabeli – działa w każdym kliencie poczty (emoji 🇵🇱 na Windows wyświetla się jako "PL").
    private static string PolishFlag() =>
        "<span style=\"display:inline-block; width:14px; height:10px; vertical-align:-1px; margin-right:2px; background-color:#dc143c; border-top:5px solid #ffffff; box-sizing:border-box; box-shadow:0 0 0 1px rgba(0,0,0,0.06);\"></span>";

    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PlPl = CultureInfo.GetCultureInfo("pl-PL");

    // Angular `date:'short'` (locale en-US) => "10/22/26, 9:20 AM"
    private static string FormatShortDate(DateTime utc) =>
        ToWarsaw(utc).ToString("M/d/yy, h:mm tt", EnUs);

    private static string FormatIssueDate(DateTime utc) =>
        ToWarsaw(utc).ToString("d MMMM yyyy", PlPl);

    private static DateTime ToWarsaw(DateTime utc)
    {
        var u = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(u, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));
        }
        catch (TimeZoneNotFoundException)
        {
            return u.ToLocalTime();
        }
    }

    private static string PluralArticles(int n) => n == 1 ? "wiadomość" : "wiadomości";

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", string.Empty) : "infoskrot.pl";

    private static string? SafeHttps(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url.Substring(7);
        }
        return url;
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
