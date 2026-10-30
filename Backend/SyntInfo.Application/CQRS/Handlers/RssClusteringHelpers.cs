using CodeHollow.FeedReader;
using SyntInfo.Application.CQRS.Commands;

namespace SyntInfo.Application.CQRS.Handlers;

/// <summary>
/// Odpowiada wyłącznie za ekstrakcję treści oraz adresów obrazków z elementów RSS (SRP)
/// oraz obsługę klastrowania wektorowego i podobieństwa kosinusowego kandydatów.
/// </summary>
internal static class RssFeedItemExtractor
{
    public static string GetBestContent(FeedItem item)
    {
        var content = item.Content?.Trim();
        var description = item.Description?.Trim();

        if (!string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(description))
        {
            return content.Length >= description.Length ? content : description;
        }

        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(description)) return description;

        return item.Title?.Trim() ?? string.Empty;
    }

    public static string? ExtractImageUrl(FeedItem item)
    {
        try
        {
            if (item.SpecificItem?.Element != null)
            {
                var el = item.SpecificItem.Element;
                var enclosure = el.Elements().FirstOrDefault(e =>
                    e.Name.LocalName.Equals("enclosure", StringComparison.OrdinalIgnoreCase) &&
                    e.Attribute("type")?.Value.StartsWith("image/") == true);

                if (enclosure != null)
                {
                    var url = enclosure.Attribute("url")?.Value;
                    if (!string.IsNullOrWhiteSpace(url)) return url;
                }

                var media = el.Elements().FirstOrDefault(e =>
                    (e.Name.LocalName.Equals("content", StringComparison.OrdinalIgnoreCase) ||
                     e.Name.LocalName.Equals("thumbnail", StringComparison.OrdinalIgnoreCase)) &&
                    (e.Attribute("medium")?.Value == "image" ||
                     e.Attribute("url")?.Value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) == true ||
                     e.Attribute("url")?.Value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true ||
                     e.Attribute("type")?.Value.StartsWith("image/") == true ||
                     e.Attribute("url") != null));

                if (media != null)
                {
                    var url = media.Attribute("url")?.Value;
                    if (!string.IsNullOrWhiteSpace(url)) return url;
                }
            }

            var contentToScan = $"{item.Description} {item.Content}";
            if (!string.IsNullOrWhiteSpace(contentToScan))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    contentToScan,
                    @"<img[^>]+src=[""']([^""']+)[""']",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    var url = match.Groups[1].Value;
                    if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        return url;
                    }
                }
            }
        }
        catch
        {
            // Silently ignore extraction issues
        }

        return null;
    }
}

internal sealed class ArticleCluster
{
    public NewsArticle? ExistingArticle { get; set; }
    public List<SummarizeArticleCommand> Candidates { get; set; } = new();
    public float[] RepresentativeEmbedding { get; set; } = Array.Empty<float>();
}

internal static class ArticleClusterConsolidator
{
    public static double CalculateCosineSimilarity(float[] vec1, float[] vec2)
    {
        if (vec1.Length != vec2.Length)
            return 0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < vec1.Length; i++)
        {
            dotProduct += vec1[i] * vec2[i];
            normA += vec1[i] * vec1[i];
            normB += vec2[i] * vec2[i];
        }

        if (normA == 0 || normB == 0)
            return 0;

        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    public static SummarizeArticleCommand Consolidate(ArticleCluster cluster)
    {
        var allUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cluster.ExistingArticle != null)
        {
            foreach (var url in cluster.ExistingArticle.SourceUrls)
            {
                allUrls.Add(url);
            }
        }

        foreach (var cand in cluster.Candidates)
        {
            allUrls.Add(cand.Url);
            if (cand.AdditionalUrls != null)
            {
                foreach (var url in cand.AdditionalUrls)
                {
                    allUrls.Add(url);
                }
            }
        }

        var urlList = allUrls.ToList();
        var primaryUrl = urlList.FirstOrDefault() ?? string.Empty;
        var additionalUrls = urlList.Skip(1).ToList();

        var representative = cluster.Candidates.OrderByDescending(c => c.Content.Length).First();
        var contentBuilder = new System.Text.StringBuilder();

        if (cluster.ExistingArticle != null)
        {
            contentBuilder.AppendLine($"[ISTNIEJĄCE PODSUMOWANIE]: {cluster.ExistingArticle.SummaryText}");
            if (!string.IsNullOrWhiteSpace(cluster.ExistingArticle.DeepContent))
            {
                contentBuilder.AppendLine($"[ISTNIEJĄCE FAKTY]: {cluster.ExistingArticle.DeepContent}");
            }
            contentBuilder.AppendLine();
        }

        contentBuilder.AppendLine("[NOWE MATERIAŁY ŹRÓDŁOWE]:");
        foreach (var cand in cluster.Candidates)
        {
            contentBuilder.AppendLine($"Nagłówek: {cand.Title}");
            contentBuilder.AppendLine($"Treść: {cand.Content}");
            contentBuilder.AppendLine("---");
        }

        var consolidatedTitle = cluster.ExistingArticle != null ? cluster.ExistingArticle.Title : representative.Title;
        var primaryImageUrl = cluster.ExistingArticle?.ImageUrl ?? cluster.Candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c.ImageUrl))?.ImageUrl;

        return new SummarizeArticleCommand(
            consolidatedTitle,
            contentBuilder.ToString(),
            primaryUrl,
            cluster.ExistingArticle != null ? cluster.ExistingArticle.PublishedAt : representative.PublishedAt,
            representative.SourceId,
            representative.Region,
            additionalUrls,
            cluster.ExistingArticle?.Id,
            primaryImageUrl
        );
    }
}

