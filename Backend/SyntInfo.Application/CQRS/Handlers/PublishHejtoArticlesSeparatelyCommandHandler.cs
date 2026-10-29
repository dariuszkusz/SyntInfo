using System.Text;
using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Commands;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

/// <summary>
/// Handler CQRS publikujący każdy artykuł jako osobny wpis na portalu Hejto wraz z natywnym załącznikiem graficznym (upload zdjęcia + publikacja).
/// </summary>
public static class PublishHejtoArticlesSeparatelyCommandHandler
{
    [MessageTimeout(300)]
    public static async Task<PublishHejtoArticlesSeparatelyResult> Handle(
        PublishHejtoArticlesSeparatelyCommand command,
        IUnitOfWork uow,
        IHejtoClient hejtoClient,
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

        logger.LogInformation("Rozpoczęto osobną publikację {Count} artykułów wraz z załącznikami na Hejto.", articles.Count);

        var postIds = new List<string>();
        var errors = new List<string>();
        var delayMs = Math.Max(0, hejtoOptions.Value.DelayBetweenPostsMs);

        for (var i = 0; i < articles.Count; i++)
        {
            var article = articles[i];
            try
            {
                string? imageUuid = null;
                if (!string.IsNullOrWhiteSpace(article.ImageUrl))
                {
                    try
                    {
                        logger.LogInformation("Przesyłanie załącznika graficznego do Hejto dla artykułu '{Title}': {Url}", article.Title, article.ImageUrl);
                        imageUuid = await hejtoClient.UploadImageAsync(article.ImageUrl, cancellationToken);
                    }
                    catch (Exception uploadEx)
                    {
                        logger.LogWarning(uploadEx, "Nie udało się przesłać obrazka do Hejto dla artykułu '{Title}'. Kontynuacja bez załącznika.", article.Title);
                    }
                }

                var postContent = FormatSingleArticleForHejto(article);
                var postId = await hejtoClient.CreatePostAsync(
                    postContent,
                    command.CommunitySlug,
                    imageUuid,
                    cancellationToken);

                postIds.Add(postId);
                logger.LogInformation(
                    "Opublikowano artykuł ({Index}/{Total}) '{Title}' na Hejto (ID: {PostId}, Załącznik: {HasAttachment}).",
                    i + 1,
                    articles.Count,
                    article.Title,
                    postId,
                    !string.IsNullOrWhiteSpace(imageUuid));

                if (delayMs > 0 && i < articles.Count - 1)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Błąd podczas publikowania artykułu '{Title}' ({ArticleId}) na Hejto.", article.Title, article.Id);
                errors.Add($"{article.Title}: {ex.Message}");
            }
        }

        if (postIds.Count == 0)
        {
            var combinedError = string.Join("; ", errors);
            return new PublishHejtoArticlesSeparatelyResult(
                Success: false,
                TotalArticlesCount: articles.Count,
                PublishedCount: 0,
                PostIds: postIds,
                Message: "Nie udało się opublikować żadnego artykułu na Hejto.",
                Error: combinedError);
        }

        var summaryMsg = $"Opublikowano osobno {postIds.Count}/{articles.Count} artykułów na Hejto.";
        return new PublishHejtoArticlesSeparatelyResult(
            Success: true,
            TotalArticlesCount: articles.Count,
            PublishedCount: postIds.Count,
            PostIds: postIds,
            Message: summaryMsg,
            Error: errors.Count > 0 ? string.Join("; ", errors) : null);
    }

    public static string FormatSingleArticleForHejto(NewsArticle article)
    {
        var sb = new StringBuilder();
        var primaryUrl = article.SourceUrls.FirstOrDefault() ?? "https://www.infoskrót.pl";
        var categoryTag = SanitizeTag(article.Category?.Name);
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

    private static string SanitizeTag(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
