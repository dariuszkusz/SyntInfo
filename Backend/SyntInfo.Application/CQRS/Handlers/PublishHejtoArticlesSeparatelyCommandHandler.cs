using System.Text;
using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

/// <summary>
/// Handler CQRS orkiestrujący publikację artykułów na Hejto za pomocą asynchronicznych opóźnionych komunikatów Wolverine (Scheduled Messages).
/// Dzięki temu wątek wykonawczy nie jest blokowany, a wpisy są publikowane z bezpiecznym interwałem zapobiegającym limitom anty-flood API Hejto.
/// </summary>
public static class PublishHejtoArticlesSeparatelyCommandHandler
{
    [MessageTimeout(60)]
    public static async Task<PublishHejtoArticlesSeparatelyResult> Handle(
        PublishHejtoArticlesSeparatelyCommand command,
        IUnitOfWork uow,
        IMessageBus bus,
        IOptions<CurationOptions> curationOptions,
        IOptions<HejtoOptions> hejtoOptions,
        ILogger<PublishHejtoArticlesSeparatelyCommand> logger,
        CancellationToken cancellationToken)
    {
        List<NewsArticle> articles;

        if (command.ArticleIds is { Count: > 0 })
        {
            var idSet = command.ArticleIds.ToHashSet();
            var fetched = await uow.NewsArticles.Query()
                .Include(a => a.Category)
                .Where(a => idSet.Contains(a.Id))
                .ToListAsync(cancellationToken);

            // Zachowaj kolejność przekazaną w ArticleIds
            var lookup = fetched.ToDictionary(a => a.Id);
            articles = command.ArticleIds
                .Where(lookup.ContainsKey)
                .Select(id => lookup[id])
                .ToList();
        }
        else
        {
            articles = await uow.NewsArticles.GetSelectedForDigestAsync(cancellationToken);
            if (articles.Count == 0)
            {
                var limit = command.Count is > 0 ? command.Count.Value : curationOptions.Value.MaxArticlesPerRun;
                articles = await uow.NewsArticles.GetActiveArticlesAsync(limit, cancellationToken);
            }
        }

        if (articles.Count == 0)
        {
            logger.LogWarning("Brak artykułów w bazie danych do osobnej publikacji na Hejto.");
            return new PublishHejtoArticlesSeparatelyResult(
                Success: false,
                TotalArticlesCount: 0,
                PublishedCount: 0,
                PostIds: Array.Empty<string>(),
                Message: "Brak artykułów w bazie danych do opublikowania na Hejto.",
                Error: "Brak artykułów.");
        }

        var delaySeconds = Math.Max(1, hejtoOptions.Value.DelayBetweenPostsSeconds);
        logger.LogInformation(
            "Harmonogramowanie asynchronicznej publikacji {Count} artykułów na Hejto z interwałem {DelaySeconds}s (bez blokowania wątku).",
            articles.Count, delaySeconds);

        for (var i = 0; i < articles.Count; i++)
        {
            var article = articles[i];
            var delay = CalculatePublicationDelay(i, delaySeconds);
            var displayPosition = ToOneBasedPosition(i);

            await bus.ScheduleAsync(
                new PublishSingleHejtoArticleCommand(
                    ArticleId: article.Id,
                    CommunitySlug: command.CommunitySlug,
                    Index: displayPosition,
                    TotalCount: articles.Count),
                delay);

            logger.LogInformation(
                "Zaplanowano publikację artykułu ({Index}/{Total}) '{Title}' ({ArticleId}) na Hejto z opóźnieniem {Delay}.",
                displayPosition, articles.Count, article.Title, article.Id, delay);
        }

        var totalDuration = CalculateTotalDuration(articles.Count, delaySeconds);
        var summaryMsg = $"Zaplanowano publikację {articles.Count} artykułów na Hejto co {delaySeconds}s (całość zajmie ~{totalDuration.TotalMinutes:F1} min bez blokowania wątku).";

        return new PublishHejtoArticlesSeparatelyResult(
            Success: true,
            TotalArticlesCount: articles.Count,
            PublishedCount: articles.Count,
            PostIds: Array.Empty<string>(),
            Message: summaryMsg);
    }

    public static string FormatSingleArticleForHejto(NewsArticle article)
    {
        var sb = new StringBuilder();
        var primaryUrl = article.SourceUrls.FirstOrDefault() ?? "https://www.infoskrót.pl";
        var categoryTag = TagFormattingHelper.SanitizeTag(article.Category?.Name);
        var regionTag = article.Region switch
        {
            SourceRegion.Poland => "polska",
            SourceRegion.World => "swiat",
            SourceRegion.Positive => "dobrewiadomosci",
            _ => "polska"
        };

        sb.AppendLine($"### {article.Title}");
        sb.AppendLine();
        sb.AppendLine(article.SummaryText.Trim());
        sb.AppendLine();
        sb.AppendLine($"🔗 Źródło: [{article.Title}]({primaryUrl})");
        sb.AppendLine("Więcej wiadomości i pełne archiwum: [www.infoskrót.pl](https://www.infoskrót.pl)");
        sb.AppendLine();

        var tags = new List<string> { "#wiadomosci", "#infoskrot", $"#{regionTag}" };
        if (!string.IsNullOrWhiteSpace(categoryTag) &&
            !tags.Contains($"#{categoryTag}", StringComparer.OrdinalIgnoreCase))
        {
            tags.Add($"#{categoryTag}");
        }
        tags.Add("#polityka");

        sb.AppendLine(string.Join(" ", tags));
        return sb.ToString();
    }

    /// <summary>
    /// Konwertuje indeks liczony od 0 na 1-bazową pozycję kolejności (numerację porządkową 1..N) dla ludzi i logów.
    /// </summary>
    private static int ToOneBasedPosition(int zeroBasedIndex) => zeroBasedIndex + 1;

    /// <summary>
    /// Wylicza opóźnienie harmonogramowania dla artykułu o danym indeksie w kolejce.
    /// </summary>
    private static TimeSpan CalculatePublicationDelay(int zeroBasedIndex, int intervalSeconds) =>
        TimeSpan.FromSeconds(zeroBasedIndex * intervalSeconds);

    /// <summary>
    /// Wylicza szacowany łączny czas trwania publikacji całej partii artykułów.
    /// </summary>
    private static TimeSpan CalculateTotalDuration(int totalCount, int intervalSeconds) =>
        totalCount <= 1 ? TimeSpan.Zero : TimeSpan.FromSeconds((totalCount - 1) * intervalSeconds);

    private static string SanitizeTag(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
