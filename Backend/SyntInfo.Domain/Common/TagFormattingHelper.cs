using SyntInfo.Domain.Entities;

namespace SyntInfo.Domain.Common;

public static class TagFormattingHelper
{
    private static readonly HashSet<string> PositiveForbiddenTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "polityka",
        "sejm",
        "wybory",
        "partia",
        "rzad",
        "rząd",
        "wojna",
        "konflikt",
        "kryzys",
        "inflacja",
        "podatki",
        "przestepczosc",
        "przestępczość",
        "wypadek",
        "katastrofa"
    };

    private static readonly Dictionary<string, string[]> CategoryToTagsMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["polityka"] = new[] { "polityka", "wydarzenia" },
        ["biznes"] = new[] { "biznes", "gospodarka", "ekonomia" },
        ["gospodarka"] = new[] { "gospodarka", "ekonomia", "finanse" },
        ["ekonomia"] = new[] { "ekonomia", "gospodarka", "finanse" },
        ["finanse"] = new[] { "finanse", "gospodarka" },
        ["tech"] = new[] { "technologia", "ciekawostki" },
        ["technologia"] = new[] { "technologia", "ciekawostki" },
        ["nauka"] = new[] { "nauka", "ciekawostki" },
        ["zdrowie"] = new[] { "zdrowie", "medycyna" },
        ["medycyna"] = new[] { "medycyna", "zdrowie", "nauka" },
        ["ekologia"] = new[] { "ekologia", "natura" },
        ["środowisko"] = new[] { "ekologia", "natura" },
        ["srodowisko"] = new[] { "ekologia", "natura" },
        ["kultura"] = new[] { "kultura", "rozrywka" },
        ["sport"] = new[] { "sport" },
        ["bezpieczeństwo"] = new[] { "bezpieczenstwo", "wydarzenia" },
        ["bezpieczenstwo"] = new[] { "bezpieczenstwo", "wydarzenia" },
        ["prawo"] = new[] { "prawo", "spoleczenstwo" },
        ["społeczeństwo"] = new[] { "spoleczenstwo", "wydarzenia" },
        ["spoleczenstwo"] = new[] { "spoleczenstwo", "wydarzenia" },
        ["energetyka"] = new[] { "energetyka", "gospodarka" },
        ["motoryzacja"] = new[] { "motoryzacja", "transport" },
        ["kosmos"] = new[] { "kosmos", "nauka", "ciekawostki" }
    };

    public static string SanitizeTag(string? text, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var clean = new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    public static string NormalizeAsciiTag(string? text, string fallback = "")
    {
        var sanitized = SanitizeTag(text, fallback);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return fallback;
        }

        return string.Create(sanitized.Length, sanitized, static (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
            {
                span[i] = src[i] switch
                {
                    'ą' => 'a',
                    'ć' => 'c',
                    'ę' => 'e',
                    'ł' => 'l',
                    'ń' => 'n',
                    'ó' => 'o',
                    'ś' => 's',
                    'ź' => 'z',
                    'ż' => 'z',
                    _ => src[i]
                };
            }
        });
    }

    public static string GetRegionTag(SourceRegion region) => region switch
    {
        SourceRegion.Poland => "polska",
        SourceRegion.World => "swiat",
        SourceRegion.Positive => "dobrewiadomosci",
        _ => "polska"
    };

    public static bool IsForbiddenForRegion(string tag, SourceRegion region)
    {
        if (region != SourceRegion.Positive)
        {
            return false;
        }

        var ascii = NormalizeAsciiTag(tag);
        var sanitized = SanitizeTag(tag);
        return PositiveForbiddenTags.Contains(ascii) || PositiveForbiddenTags.Contains(sanitized);
    }

    public static List<string> SanitizeAndFilterTags(IEnumerable<string>? rawTags, SourceRegion region)
    {
        var result = new List<string>();
        if (rawTags == null)
        {
            return result;
        }

        foreach (var raw in rawTags)
        {
            var tag = NormalizeAsciiTag(raw);
            if (string.IsNullOrWhiteSpace(tag) || tag.Length < 2)
            {
                continue;
            }

            if (IsForbiddenForRegion(tag, region))
            {
                continue;
            }

            if (!result.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    /// <summary>
    /// Buduje pełną listę hashtagów (ze znakiem #) dla pojedynczego artykułu w podejściu hybrydowym:
    /// 1. Tagi stałe (#wiadomosci, #infoskrot) + tag sekcji (#polska / #swiat / #dobrewiadomosci).
    /// 2. Tagi wynikające ze słownika kategorii (np. BIZNES -> #biznes #gospodarka).
    /// 3. Tagi kontekstowe wygenerowane przez AI (zapisane w article.Tags).
    /// 4. Strażnik sekcji (Guard Rule): sekcja Positive nigdy nie otrzymuje tagu #polityka ani tagów kryzysowych,
    ///    a sekcje Poland/World otrzymują #polityka tylko wtedy, gdy wynika to z kategorii lub tagów artykułu.
    /// </summary>
    public static IReadOnlyList<string> BuildHashtagsForArticle(NewsArticle article, int maxTags = 7)
    {
        ArgumentNullException.ThrowIfNull(article);

        var rawTags = new List<string>
        {
            "wiadomosci",
            "infoskrot",
            GetRegionTag(article.Region)
        };

        if (article.Region == SourceRegion.Positive)
        {
            rawTags.Add("pozytywnie");
        }

        var categoryKey = NormalizeAsciiTag(article.Category?.Name);
        if (!string.IsNullOrWhiteSpace(categoryKey))
        {
            if (CategoryToTagsMap.TryGetValue(categoryKey, out var mappedTags))
            {
                rawTags.AddRange(mappedTags);
            }
            else if (categoryKey is not ("general" or "pl" or "swiat" or "wiadomosci"))
            {
                rawTags.Add(categoryKey);
            }
        }

        if (article.Tags is { Count: > 0 })
        {
            rawTags.AddRange(article.Tags);
        }

        var filtered = new List<string>();
        foreach (var candidate in rawTags)
        {
            var clean = NormalizeAsciiTag(candidate);
            if (string.IsNullOrWhiteSpace(clean))
            {
                continue;
            }

            if (IsForbiddenForRegion(clean, article.Region))
            {
                continue;
            }

            var formatted = $"#{clean}";
            if (!filtered.Contains(formatted, StringComparer.OrdinalIgnoreCase))
            {
                filtered.Add(formatted);
            }

            if (filtered.Count >= maxTags)
            {
                break;
            }
        }

        return filtered;
    }

    public static string FormatHashtagsLineForArticle(NewsArticle article, int maxTags = 7)
    {
        ArgumentNullException.ThrowIfNull(article);
        return string.Join(" ", BuildHashtagsForArticle(article, maxTags));
    }

    /// <summary>
    /// Buduje dynamiczną listę hashtagów dla wydania zbiorczego (Digest) na podstawie przekrojowej zawartości artykułów.
    /// Nie dodaje #polityka, jeśli żaden artykuł z sekcji Poland/World nie dotyczy polityki.
    /// </summary>
    public static IReadOnlyList<string> BuildHashtagsForDigest(IReadOnlyList<NewsArticle> articles, int maxTags = 7)
    {
        var result = new List<string> { "#wiadomosci", "#infoskrot" };
        if (articles == null || articles.Count == 0)
        {
            return result;
        }

        foreach (var region in articles.Select(a => a.Region).Distinct())
        {
            var regionTag = $"#{GetRegionTag(region)}";
            if (!result.Contains(regionTag, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(regionTag);
            }
        }

        var topicCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var article in articles)
        {
            var articleHashtags = BuildHashtagsForArticle(article, maxTags: 8);
            foreach (var tag in articleHashtags)
            {
                if (result.Contains(tag, StringComparer.OrdinalIgnoreCase) ||
                    string.Equals(tag, "#pozytywnie", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                topicCounts[tag] = topicCounts.TryGetValue(tag, out var count) ? count + 1 : 1;
            }
        }

        var topTopics = topicCounts
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => kvp.Key);

        foreach (var topicTag in topTopics)
        {
            if (result.Count >= maxTags)
            {
                break;
            }

            result.Add(topicTag);
        }

        return result;
    }

    public static string FormatHashtagsLineForDigest(IReadOnlyList<NewsArticle> articles, int maxTags = 7) =>
        string.Join(" ", BuildHashtagsForDigest(articles, maxTags));
}
