using Microsoft.EntityFrameworkCore;
using CodeHollow.FeedReader;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Wolverine.Attributes;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Tavily;
using System.Net.Http.Json;
using Pgvector.EntityFrameworkCore;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class TriggerRssFetchCommandHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly IMessageBus _bus;
        private readonly IOpenRouterClient _openRouterClient;
        private readonly IGoogleAiStudioClient _googleAiStudioClient;
        private readonly ILogger<TriggerRssFetchCommandHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public TriggerRssFetchCommandHandler(
            IUnitOfWork uow,
            IMessageBus bus,
            IOpenRouterClient openRouterClient,
            IGoogleAiStudioClient googleAiStudioClient,
            ILogger<TriggerRssFetchCommandHandler> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _uow = uow;
            _bus = bus;
            _openRouterClient = openRouterClient;
            _googleAiStudioClient = googleAiStudioClient;
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        [MessageTimeout(1800)] // 30 minut (potrzebne, bo artykuły są przetwarzane sekwencyjnie przez InvokeAsync)
        public async Task Handle(TriggerRssFetchCommand _, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Rozpoczęto sprawdzanie feedów RSS.");

            // Dezaktywacja artykułów starszych niż 24h
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

            var sources = await _uow.Repository<NewsSource>().Query()
                .Where(s => s.IsActive)
                .ToListAsync(cancellationToken);

            var newArticlesToProcess = new List<SummarizeArticleCommand>();

            foreach (var source in sources)
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(30);

                    // Lepsze nagłówki, aby uniknąć 403 Forbidden (np. TVN24)
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
                    client.DefaultRequestHeaders.Add("Accept", "application/xml, text/xml, application/rss+xml, */*");
                    client.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
                    client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
                    client.DefaultRequestHeaders.Add("Pragma", "no-cache");

                    var currentUrl = source.RssUrl;
                    HttpResponseMessage response = null!;
                    int redirectCount = 0;
                    const int maxRedirects = 5;

                    while (redirectCount <= maxRedirects)
                    {
                        response = await client.GetAsync(currentUrl, cancellationToken);
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        {
                            var location = response.Headers.Location;
                            if (location != null)
                            {
                                if (!location.IsAbsoluteUri)
                                {
                                    location = new Uri(new Uri(currentUrl), location);
                                }
                                currentUrl = location.ToString();
                                redirectCount++;
                                _logger.LogInformation("Podążanie za przekierowaniem RSS ({Count}/{Max}) do: {RedirectUrl}", redirectCount, maxRedirects, currentUrl);
                                continue;
                            }
                        }
                        break;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            _logger.LogWarning("Źródło RSS nie zostało znalezione (404): {SourceUrl}", source.RssUrl);
                        else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                            _logger.LogWarning("Dostęp do RSS zabroniony (403). Próba obejścia nagłówkami nie powiodła się: {SourceUrl}", source.RssUrl);
                        else if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                            _logger.LogWarning("RSS zwrócił przekierowanie {StatusCode} ({CodeInt}), którego HttpClient nie podążył (przekroczono limit lub brak nagłówka Location): {SourceUrl}", response.StatusCode, (int)response.StatusCode, source.RssUrl);
                        else
                            _logger.LogWarning("Błąd podczas pobierania RSS {StatusCode}: {SourceUrl}", response.StatusCode, source.RssUrl);

                        continue;
                    }

                    var rssContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var feed = FeedReader.ReadFromString(rssContent);

                    foreach (var item in feed.Items.Take(20))
                    {
                        var url = item.Link;
                        var exists = await _uow.Repository<NewsArticle>().Query()
                            .AnyAsync(a => a.SourceUrls.Contains(url) || a.Title == item.Title, cancellationToken);

                        if (!exists)
                        {
                            var content = !string.IsNullOrWhiteSpace(item.Description) ? item.Description : item.Content;
                            if (string.IsNullOrWhiteSpace(content)) content = item.Title;

                            newArticlesToProcess.Add(new SummarizeArticleCommand(
                                item.Title,
                                content,
                                url,
                                item.PublishingDate ?? DateTime.UtcNow,
                                source.Id,
                                source.Region
                            ));
                        }
                    }

                    source.LastFetchedAt = DateTime.UtcNow;
                    _uow.Repository<NewsSource>().Update(source);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Wystąpił błąd podczas pobierania feedu z {SourceUrl}", source.RssUrl);
                }
            }

            await _uow.SaveChangesAsync(cancellationToken);

            var maxPerRegion = _configuration.GetValue("ProcessingSettings:MaxArticlesPerRegion", 10);

            // Jesli RSS nie zwrócił nic dla danego regionu, użyj Discovery przez Tavily
            if (!newArticlesToProcess.Any(a => a.Region == SourceRegion.Poland))
            {
                _logger.LogInformation("Brak nowych artykułów z RSS dla Polski. Uruchamiam Discovery przez Tavily...");
                var discovered = await DiscoverNewsWithTavilyAsync(SourceRegion.Poland, cancellationToken);
                newArticlesToProcess.AddRange(discovered);
            }

            if (!newArticlesToProcess.Any(a => a.Region == SourceRegion.World))
            {
                _logger.LogInformation("Brak nowych artykułów z RSS dla Świata. Uruchamiam Discovery przez Tavily...");
                var discovered = await DiscoverNewsWithTavilyAsync(SourceRegion.World, cancellationToken);
                newArticlesToProcess.AddRange(discovered);
            }

            _logger.LogInformation("Rozpoczęto generowanie embeddingów dla {Count} nowych artykułów.", newArticlesToProcess.Count);
            var candidatesWithEmbeddings = new List<(SummarizeArticleCommand Command, float[] Embedding)>();
            foreach (var candidate in newArticlesToProcess)
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

            _logger.LogInformation("Rozpoczęto klastrowanie semantyczne dla {Count} artykułów z embeddingami.", candidatesWithEmbeddings.Count);
            var clusteredCandidates = await ClusterCandidatesAndDatabaseAsync(candidatesWithEmbeddings, cancellationToken);

            var allCommands = new List<SummarizeArticleCommand>();
            allCommands.AddRange(await GetCommandsToExecuteAsync(SourceRegion.World, clusteredCandidates.Where(a => a.Region == SourceRegion.World).ToList(), maxPerRegion, cancellationToken));
            allCommands.AddRange(await GetCommandsToExecuteAsync(SourceRegion.Poland, clusteredCandidates.Where(a => a.Region == SourceRegion.Poland).ToList(), maxPerRegion, cancellationToken));

            if (allCommands.Any())
            {
                _logger.LogInformation("Wywoływanie SummarizeArticleCommand dla {Count} skonsolidowanych klastrów.", allCommands.Count);
                foreach (var cmd in allCommands)
                {
                    await _bus.InvokeAsync(cmd, cancellationToken);
                }
            }
        }

        private async Task<List<SummarizeArticleCommand>> DiscoverNewsWithTavilyAsync(SourceRegion region, CancellationToken cancellationToken)
        {
            var discovered = new List<SummarizeArticleCommand>();
            try
            {
                string query = region == SourceRegion.Poland
                    ? "najważniejsze wiadomości z Polski z ostatnich 24 godzin"
                    : "top world news headlines last 24 hours";

                // Używamy bezpośrednio HttpClient do Tavily, aby dostać listę linków, a nie tylko Answer
                var apiKey = _configuration["Search:TavilyApiKey"];
                if (string.IsNullOrEmpty(apiKey) || apiKey.Contains("PLACEHOLDER")) return discovered;

                var client = _httpClientFactory.CreateClient();
                var request = new
                {
                    api_key = apiKey,
                    query = query,
                    search_depth = "advanced",
                    max_results = 8
                };

                var response = await client.PostAsJsonAsync("https://api.tavily.com/search", request, cancellationToken);
                if (!response.IsSuccessStatusCode) return discovered;

                var result = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(cancellationToken: cancellationToken);
                if (result?.Results == null) return discovered;

                foreach (var res in result.Results)
                {
                    // Sprawdź czy już nie mamy tego linku
                    var exists = await _uow.Repository<NewsArticle>().Query()
                        .AnyAsync(a => a.SourceUrls.Contains(res.Url) || a.Title == res.Title, cancellationToken);

                    if (!exists)
                    {
                        discovered.Add(new SummarizeArticleCommand(
                            res.Title,
                            res.Content, // Tavily daje fragment treści
                            res.Url,
                            DateTime.UtcNow,
                            Guid.Empty, // Brak konkretnego źródła RSS
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

        private async Task<List<SummarizeArticleCommand>> GetCommandsToExecuteAsync(SourceRegion region, List<SummarizeArticleCommand> newCandidates, int maxPerRegion, CancellationToken cancellationToken)
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
                var prompt = $"Analizujesz newsy dla regionu {region}. Z poniższej listy JSON wybierz DOKŁADNIE {maxPerRegion} najważniejszych newsów. Zwróć tylko tablicę numerów index, np. [1, 5, 12].\n\n{jsonStr}";

                var selectedIndexes = await _googleAiStudioClient.SelectTopArticlesIndexesAsync(prompt, maxPerRegion, region, cancellationToken);

                if (selectedIndexes == null || !selectedIndexes.Any())
                {
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

                // Szukamy w bazie artykułów z tego samego dnia o podobieństwie > 0.82 (CosineDistance < 0.18)
                var similarDbArticles = await _uow.Repository<NewsArticle>().Query()
                    .Where(a => a.Embedding != null
                             && a.PublishedAt >= startOfDay
                             && a.PublishedAt < endOfDay
                             && a.Embedding.CosineDistance(targetVector) < 0.18)
                    .ToListAsync(cancellationToken);

                var primaryDbArticle = similarDbArticles.FirstOrDefault();

                if (primaryDbArticle != null)
                {
                    // Szukamy istniejącego klastra powiązanego z tym samym artykułem z bazy
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
                    // Szukamy pasującego klastra wśród już utworzonych
                    ArticleCluster? matchedCluster = null;
                    foreach (var cluster in clusters)
                    {
                        double similarity = 0;
                        if (cluster.ExistingArticle != null && cluster.ExistingArticle.Embedding != null)
                        {
                            similarity = CalculateCosineSimilarity(embedding, cluster.ExistingArticle.Embedding.ToArray());
                        }
                        else
                        {
                            similarity = CalculateCosineSimilarity(embedding, cluster.RepresentativeEmbedding);
                        }

                        if (similarity > 0.82)
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

            var consolidatedCommands = new List<SummarizeArticleCommand>();
            foreach (var cluster in clusters)
            {
                consolidatedCommands.Add(ConsolidateCluster(cluster));
            }

            return consolidatedCommands;
        }

        private SummarizeArticleCommand ConsolidateCluster(ArticleCluster cluster)
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

            return new SummarizeArticleCommand(
                consolidatedTitle,
                contentBuilder.ToString(),
                primaryUrl,
                cluster.ExistingArticle != null ? cluster.ExistingArticle.PublishedAt : representative.PublishedAt,
                representative.SourceId,
                representative.Region,
                additionalUrls,
                cluster.ExistingArticle?.Id
            );
        }

        private class ArticleCluster
        {
            public NewsArticle? ExistingArticle { get; set; }
            public List<SummarizeArticleCommand> Candidates { get; set; } = new();
            public float[] RepresentativeEmbedding { get; set; } = Array.Empty<float>();
        }

        private static double CalculateCosineSimilarity(float[] vec1, float[] vec2)
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
    }
}
