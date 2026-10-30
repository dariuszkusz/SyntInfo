using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;
using System.Text.Json;
using Wolverine.Attributes;
using Pgvector.EntityFrameworkCore;

namespace SyntInfo.Application.CQRS.Handlers;

public class TriggerRssFetchCommandHandler
{
    private readonly IUnitOfWork _uow;
    private readonly IMessageBus _bus;
    private readonly IOpenRouterClient _openRouterClient;
    private readonly IGoogleAiStudioClient _googleAiStudioClient;
    private readonly ILogger<TriggerRssFetchCommandHandler> _logger;
    private readonly ProcessingOptions _options;
    private readonly IRssFeedClient _rssFeedClient;
    private readonly INewsDiscoveryService _newsDiscoveryService;

    public TriggerRssFetchCommandHandler(
        IUnitOfWork uow,
        IMessageBus bus,
        IOpenRouterClient openRouterClient,
        IGoogleAiStudioClient googleAiStudioClient,
        ILogger<TriggerRssFetchCommandHandler> logger,
        IOptions<ProcessingOptions> options,
        IRssFeedClient rssFeedClient,
        INewsDiscoveryService newsDiscoveryService)
    {
        _uow = uow;
        _bus = bus;
        _openRouterClient = openRouterClient;
        _googleAiStudioClient = googleAiStudioClient;
        _logger = logger;
        _options = options.Value;
        _rssFeedClient = rssFeedClient;
        _newsDiscoveryService = newsDiscoveryService;
    }

    [MessageTimeout(1800)] // 30 minut (potrzebne, bo artykuły są przetwarzane sekwencyjnie przez InvokeAsync)
    public async Task Handle(TriggerRssFetchCommand _, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Rozpoczęto sprawdzanie feedów RSS.");

        // 1. Dezaktywacja artykułów starszych niż 24h
        await DeactivateExpiredNews(cancellationToken);

        // 2. Pobranie nowych artykułów z feedów RSS
        var newArticles = await FetchRssArticlesAsync(cancellationToken);

        // 3. Uzupełnienie brakujących regionów przez Tavily Discovery
        await FillMissingRegionsWithDiscoveryAsync(newArticles, cancellationToken);

        // 4. Generowanie wektorów (embeddingów) dla nowych artykułów
        var candidatesWithEmbeddings = await GenerateCandidateEmbeddingsAsync(newArticles, cancellationToken);

        // 5. Klastrowanie semantyczne i deduplikacja z artykułami w bazie
        var clusteredCandidates = await ClusterCandidatesAndDatabaseAsync(candidatesWithEmbeddings, cancellationToken);

        // 6. Selekcja najważniejszych artykułów per region (z udziałem AI)
        var commandsToExecute = await SelectArticlesToSummarizeAsync(clusteredCandidates, cancellationToken);

        // 7. Wysłanie komend do streszczenia na szynę wiadomości
        await DispatchSummarizeCommandsAsync(commandsToExecute, cancellationToken);
    }

    private async Task<List<SummarizeArticleCommand>> FetchRssArticlesAsync(CancellationToken cancellationToken)
    {
        var sources = await _uow.NewsSources.GetActiveSourcesAsync(cancellationToken);
        var articles = new List<SummarizeArticleCommand>();

        foreach (var source in sources)
        {
            try
            {
                var feed = await _rssFeedClient.FetchFeedAsync(source.RssUrl, cancellationToken);
                if (feed == null)
                {
                    continue;
                }

                foreach (var item in feed.Items.Take(20))
                {
                    var url = item.Link;
                    var exists = await _uow.NewsArticles.ExistsByUrlOrTitleAsync(url, item.Title, cancellationToken);

                    if (!exists)
                    {
                        var content = RssFeedItemExtractor.GetBestContent(item);
                        var imageUrl = RssFeedItemExtractor.ExtractImageUrl(item);

                        articles.Add(new SummarizeArticleCommand(
                            item.Title,
                            content,
                            url,
                            item.PublishingDate ?? DateTime.UtcNow,
                            source.Id,
                            source.Region,
                            null,
                            null,
                            imageUrl
                        ));
                    }
                }

                source.LastFetchedAt = DateTime.UtcNow;
                _uow.NewsSources.Update(source);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas przetwarzania feedu ze źródła {SourceName} ({SourceUrl})", source.Name, source.RssUrl);
            }
        }

        await _uow.SaveChangesAsync(cancellationToken);
        return articles;
    }

    private async Task FillMissingRegionsWithDiscoveryAsync(
        List<SummarizeArticleCommand> articles,
        CancellationToken cancellationToken)
    {
        foreach (var region in Enum.GetValues<SourceRegion>())
        {
            if (!articles.Any(a => a.Region == region))
            {
                _logger.LogInformation("Brak nowych artykułów z RSS dla regionu {Region}. Uruchamiam Discovery przez Tavily...", region);
                var discovered = await DiscoverNewsWithTavilyAsync(region, cancellationToken);
                articles.AddRange(discovered);
            }
        }
    }

    private async Task<List<(SummarizeArticleCommand Command, float[] Embedding)>> GenerateCandidateEmbeddingsAsync(
        List<SummarizeArticleCommand> candidates,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Rozpoczęto generowanie embeddingów dla {Count} nowych artykułów.", candidates.Count);
        var candidatesWithEmbeddings = new List<(SummarizeArticleCommand Command, float[] Embedding)>();

        foreach (var candidate in candidates)
        {
            try
            {
                var textToEmbed = $"{candidate.Title} {candidate.Content}".Trim();
                var embedding = await _openRouterClient.GenerateEmbeddingsAsync(textToEmbed, cancellationToken);
                if (embedding != null && embedding.Length > 0)
                {
                    candidatesWithEmbeddings.Add((candidate, embedding));
                }
                else
                {
                    _logger.LogWarning("Wygenerowano pusty embedding dla artykułu: {Title}", candidate.Title);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd generowania embeddingu dla artykułu: {Title}", candidate.Title);
            }
        }

        return candidatesWithEmbeddings;
    }

    private async Task DispatchSummarizeCommandsAsync(
        IReadOnlyCollection<SummarizeArticleCommand> commands,
        CancellationToken cancellationToken)
    {
        if (!commands.Any())
        {
            return;
        }

        _logger.LogInformation("Wywoływanie SummarizeArticleCommand dla {Count} skonsolidowanych klastrów.", commands.Count);
        foreach (var cmd in commands)
        {
            await _bus.InvokeAsync(cmd, cancellationToken);
        }
    }

    private async Task<List<SummarizeArticleCommand>> SelectArticlesToSummarizeAsync(
        List<SummarizeArticleCommand> clusteredCandidates,
        CancellationToken cancellationToken)
    {
        var commands = new List<SummarizeArticleCommand>();

        foreach (var region in Enum.GetValues<SourceRegion>())
        {
            try
            {
                var candidatesForRegion = clusteredCandidates.Where(a => a.Region == region).ToList();
                var limit = region == SourceRegion.Positive ? _options.MaxPositiveArticles : _options.MaxArticlesPerRegion;

                var selected = await GetCommandsToExecuteAsync(region, candidatesForRegion, limit, cancellationToken);
                commands.AddRange(selected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nieobsłużony błąd podczas selekcji artykułów dla regionu {Region}", region);
            }
        }

        return commands;
    }

    private async Task DeactivateExpiredNews(CancellationToken cancellationToken)
    {
        var expiredCutoff = DateTime.UtcNow.AddHours(-24);
        var expiredArticles = await _uow.Repository<NewsArticle>().Query()
            .Where(a => a.IsActive && a.PublishedAt < expiredCutoff)
            .ToListAsync(cancellationToken);

        if (expiredArticles.Any())
        {
            _logger.LogInformation("Dezaktywacja {Count} przestarzałych artykułów.", expiredArticles.Count);
            foreach (var article in expiredArticles)
            {
                article.IsActive = false;
                _uow.Repository<NewsArticle>().Update(article);
            }
            await _uow.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<List<SummarizeArticleCommand>> DiscoverNewsWithTavilyAsync(SourceRegion region, CancellationToken cancellationToken)
    {
        var discovered = new List<SummarizeArticleCommand>();
        try
        {
            var items = await _newsDiscoveryService.DiscoverNewsAsync(region, cancellationToken);
            foreach (var item in items)
            {
                var exists = await _uow.NewsArticles.ExistsByUrlOrTitleAsync(item.Url, item.Title, cancellationToken);

                if (!exists)
                {
                    discovered.Add(new SummarizeArticleCommand(
                        item.Title,
                        item.Content,
                        item.Url,
                        item.PublishedAt,
                        Guid.Empty,
                        region
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas Discovery dla regionu {Region}", region);
        }

        return discovered;
    }

    private async Task<List<SummarizeArticleCommand>> GetCommandsToExecuteAsync(
        SourceRegion region,
        List<SummarizeArticleCommand> newCandidates,
        int maxPerRegion,
        CancellationToken cancellationToken)
    {
        var commandsToExecute = new List<SummarizeArticleCommand>();
        try
        {
            var cutoffDate = DateTime.UtcNow.AddHours(-24);
            var oldActiveArticles = await _uow.Repository<NewsArticle>().Query()
                .Where(a => a.Region == region && a.IsActive && a.PublishedAt >= cutoffDate)
                .ToListAsync(cancellationToken);

            if (newCandidates == null || !newCandidates.Any())
            {
                return commandsToExecute;
            }

            var limitedOldArticles = oldActiveArticles.OrderByDescending(a => a.PublishedAt).Take(20).ToList();
            var limitedNewCandidates = newCandidates.OrderByDescending(c => c.PublishedAt).Take(50).ToList();

            var selectionList = new List<object>();
            foreach (var old in limitedOldArticles)
            {
                selectionList.Add(new { index = selectionList.Count, title = old.Title, type = "old" });
            }
            int newStartIndex = selectionList.Count;
            foreach (var cand in limitedNewCandidates)
            {
                selectionList.Add(new { index = selectionList.Count, title = cand.Title, type = "new" });
            }

            if (selectionList.Count <= maxPerRegion)
            {
                commandsToExecute.AddRange(limitedNewCandidates);
                return commandsToExecute;
            }

            var jsonStr = JsonSerializer.Serialize(selectionList);
            var selectedIndexes = await _googleAiStudioClient.SelectTopArticlesIndexesAsync(jsonStr, maxPerRegion, region, cancellationToken);

            if (selectedIndexes == null || !selectedIndexes.Any())
            {
                if (region == SourceRegion.Positive)
                {
                    _logger.LogInformation("Brak kandydatów spełniających rygorystyczne kryteria pozytywnych wiadomości.");
                    return commandsToExecute;
                }

                _logger.LogWarning("Brak wybranych indeksów przez AI dla regionu {Region}. Fallback logiczny: wybór {Count} najnowszych artykułów.", region, Math.Min(limitedNewCandidates.Count, maxPerRegion));
                commandsToExecute.AddRange(limitedNewCandidates.Take(maxPerRegion));
                return commandsToExecute;
            }

            foreach (var i in selectedIndexes)
            {
                if (i >= newStartIndex && i < selectionList.Count)
                {
                    commandsToExecute.Add(limitedNewCandidates[i - newStartIndex]);
                }
            }

            return commandsToExecute;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd w GetCommandsToExecuteAsync dla regionu {Region}", region);

            if (region != SourceRegion.Positive && newCandidates != null && newCandidates.Any())
            {
                var fallbackCount = Math.Min(newCandidates.Count, maxPerRegion);
                _logger.LogWarning("Uruchomiono bezpieczny fallback logiczny dla regionu {Region}: wybór {Count} najświeższych artykułów.", region, fallbackCount);
                return newCandidates.OrderByDescending(c => c.PublishedAt).Take(maxPerRegion).ToList();
            }

            return commandsToExecute;
        }
    }

    private async Task<List<SummarizeArticleCommand>> ClusterCandidatesAndDatabaseAsync(
        List<(SummarizeArticleCommand Command, float[] Embedding)> candidatesWithEmbeddings,
        CancellationToken cancellationToken)
    {
        var clusters = new List<ArticleCluster>();

        foreach (var (candidate, embedding) in candidatesWithEmbeddings)
        {
            var targetVector = new Pgvector.Vector(embedding);
            var startOfDay = candidate.PublishedAt.Date.ToUniversalTime();
            var endOfDay = startOfDay.AddDays(1);

            var similarDbArticles = await _uow.Repository<NewsArticle>().Query()
                .Where(a => a.Embedding != null
                         && a.PublishedAt >= startOfDay
                         && a.PublishedAt < endOfDay
                         && a.Embedding.CosineDistance(targetVector) < 0.18)
                .ToListAsync(cancellationToken);

            var primaryDbArticle = similarDbArticles.FirstOrDefault();

            if (primaryDbArticle != null)
            {
                var existingCluster = clusters.FirstOrDefault(c => c.ExistingArticle != null && c.ExistingArticle.Id == primaryDbArticle.Id);
                if (existingCluster != null)
                {
                    existingCluster.Candidates.Add(candidate);
                }
                else
                {
                    clusters.Add(new ArticleCluster
                    {
                        ExistingArticle = primaryDbArticle,
                        Candidates = new List<SummarizeArticleCommand> { candidate },
                        RepresentativeEmbedding = embedding
                    });
                }
            }
            else
            {
                ArticleCluster? matchedCluster = null;
                foreach (var cluster in clusters)
                {
                    var referenceVec = cluster.ExistingArticle?.Embedding != null
                        ? cluster.ExistingArticle.Embedding.ToArray()
                        : cluster.RepresentativeEmbedding;

                    if (ArticleClusterConsolidator.CalculateCosineSimilarity(embedding, referenceVec) > 0.82)
                    {
                        matchedCluster = cluster;
                        break;
                    }
                }

                if (matchedCluster != null)
                {
                    matchedCluster.Candidates.Add(candidate);
                }
                else
                {
                    clusters.Add(new ArticleCluster
                    {
                        ExistingArticle = null,
                        Candidates = new List<SummarizeArticleCommand> { candidate },
                        RepresentativeEmbedding = embedding
                    });
                }
            }
        }

        return clusters.Select(ArticleClusterConsolidator.Consolidate).ToList();
    }
}
