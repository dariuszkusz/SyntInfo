using System.Text;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Infrastructure.Services.Newsletter;

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
                sb.Append(NewsletterHtmlRenderer.Render(articles.Select(a => NewsletterArticle.From(a)).ToList()));
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

