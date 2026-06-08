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
using Pgvector;

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

                    var response = await client.GetAsync(source.RssUrl, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            _logger.LogWarning("Źródło RSS nie zostało znalezione (404): {SourceUrl}", source.RssUrl);
                        else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                            _logger.LogWarning("Dostęp do RSS zabroniony (403). Próba obejścia nagłówkami nie powiodła się: {SourceUrl}", source.RssUrl);
                        else if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                            _logger.LogWarning("RSS zwrócił przekierowanie {StatusCode} ({CodeInt}), którego HttpClient nie podążył: {SourceUrl}", response.StatusCode, (int)response.StatusCode, source.RssUrl);
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

            // KROK 1: Pre-processing deduplikacja (porównanie kandydatów ze sobą oraz z bazą)
            var deduplicatedCandidates = await PreprocessDeduplicateCandidatesAsync(newArticlesToProcess, cancellationToken);

            var allCommands = new List<SummarizeArticleCommand>();
            allCommands.AddRange(await GetCommandsToExecuteAsync(SourceRegion.World, deduplicatedCandidates.Where(a => a.Region == SourceRegion.World).ToList(), maxPerRegion, cancellationToken));
            allCommands.AddRange(await GetCommandsToExecuteAsync(SourceRegion.Poland, deduplicatedCandidates.Where(a => a.Region == SourceRegion.Poland).ToList(), maxPerRegion, cancellationToken));

            if (allCommands.Any())
            {
                foreach (var cmd in allCommands)
                {
                    await _bus.InvokeAsync(cmd, cancellationToken);
                }
            }

            // KROK 2: Post-processing deduplikacja (scalenie nowo dodanych i istniejących artykułów po podsumowaniu)
            await PostprocessDeduplicateActiveArticlesAsync(cancellationToken);
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

                var selectedIndexes = await _googleAiStudioClient.SelectTopArticlesIndexesAsync(prompt, maxPerRegion, cancellationToken);

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

        private async Task<List<SummarizeArticleCommand>> PreprocessDeduplicateCandidatesAsync(List<SummarizeArticleCommand> candidates, CancellationToken cancellationToken)
        {
            if (candidates == null || !candidates.Any())
                return new List<SummarizeArticleCommand>();

            _logger.LogInformation("[Pre-Process] Starting preprocessing deduplication for {Count} candidates.", candidates.Count);

            var cutoffDate = DateTime.UtcNow.AddHours(-24);
            var activeArticles = await _uow.Repository<NewsArticle>().Query()
                .Where(a => a.IsActive && a.PublishedAt >= cutoffDate)
                .ToListAsync(cancellationToken);

            var remainingCandidates = new List<(SummarizeArticleCommand Cmd, float[] Embedding)>();

            foreach (var candidate in candidates)
            {
                // Generuj embedding tytułu
                float[] candidateEmbedding;
                try
                {
                    candidateEmbedding = await _openRouterClient.GenerateEmbeddingsAsync(candidate.Title, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to generate title embedding for candidate: {Title}. Skipping semantic check.", candidate.Title);
                    remainingCandidates.Add((candidate, Array.Empty<float>()));
                    continue;
                }

                if (candidateEmbedding.Length == 0)
                {
                    remainingCandidates.Add((candidate, Array.Empty<float>()));
                    continue;
                }

                // Sprawdź czy pasuje do jakiegoś artykułu z bazy
                var matchedDbArticle = activeArticles
                    .Where(a => a.Region == candidate.Region && a.Embedding != null)
                    .Select(a => new { Article = a, Similarity = CalculateCosineSimilarity(candidateEmbedding, a.Embedding!.ToArray()) })
                    .Where(x => x.Similarity >= 0.85) // próg podobieństwa tytułu do streszczenia (domyślnie 0.85)
                    .OrderByDescending(x => x.Similarity)
                    .FirstOrDefault();

                if (matchedDbArticle != null)
                {
                    _logger.LogInformation("[Pre-Process] Candidate '{CandidateTitle}' matched existing article '{DbTitle}' (similarity: {Similarity:F2}). Merging URL.", candidate.Title, matchedDbArticle.Article.Title, matchedDbArticle.Similarity);
                    
                    var newUrls = new List<string>(matchedDbArticle.Article.SourceUrls);
                    if (!newUrls.Contains(candidate.Url))
                    {
                        newUrls.Add(candidate.Url);
                    }
                    if (candidate.AdditionalUrls != null)
                    {
                        foreach (var u in candidate.AdditionalUrls)
                        {
                            if (!newUrls.Contains(u))
                                newUrls.Add(u);
                        }
                    }

                    matchedDbArticle.Article.SourceUrls = newUrls;
                    _uow.Repository<NewsArticle>().Update(matchedDbArticle.Article);
                }
                else
                {
                    remainingCandidates.Add((candidate, candidateEmbedding));
                }
            }

            // Zapiszmy zmiany w bazie (dla tych podpiętych URLi do istniejących artykułów)
            await _uow.SaveChangesAsync(cancellationToken);

            // Teraz deduplikacja wewnątrz paczki kandydatów
            var uniqueCandidates = new List<(SummarizeArticleCommand Cmd, float[] Embedding)>();
            foreach (var item in remainingCandidates)
            {
                if (item.Embedding.Length == 0)
                {
                    uniqueCandidates.Add(item);
                    continue;
                }

                var duplicate = uniqueCandidates
                    .Where(x => x.Cmd.Region == item.Cmd.Region && x.Embedding.Length > 0)
                    .Select(x => new { Unique = x, Similarity = CalculateCosineSimilarity(item.Embedding, x.Embedding) })
                    .Where(x => x.Similarity >= 0.85) // próg podobieństwa tytułów między sobą (domyślnie 0.85)
                    .OrderByDescending(x => x.Similarity)
                    .FirstOrDefault();

                if (duplicate != null)
                {
                    _logger.LogInformation("[Pre-Process] Candidate '{CandidateTitle}' duplicate of candidate '{UniqueTitle}' (similarity: {Similarity:F2}). Merging URLs.", item.Cmd.Title, duplicate.Unique.Cmd.Title, duplicate.Similarity);
                    
                    var urls = duplicate.Unique.Cmd.AdditionalUrls ?? new List<string>();
                    if (!urls.Contains(item.Cmd.Url))
                        urls.Add(item.Cmd.Url);
                    if (item.Cmd.AdditionalUrls != null)
                    {
                        foreach (var u in item.Cmd.AdditionalUrls)
                        {
                            if (!urls.Contains(u))
                                urls.Add(u);
                        }
                    }

                    var updatedCmd = duplicate.Unique.Cmd with { AdditionalUrls = urls };
                    var idx = uniqueCandidates.IndexOf(duplicate.Unique);
                    uniqueCandidates[idx] = (updatedCmd, duplicate.Unique.Embedding);
                }
                else
                {
                    uniqueCandidates.Add(item);
                }
            }

            return uniqueCandidates.Select(x => x.Cmd).ToList();
        }

        private async Task PostprocessDeduplicateActiveArticlesAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[Post-Process] Starting post-processing deduplication.");
            var cutoffDate = DateTime.UtcNow.AddHours(-24);
            var activeArticles = await _uow.Repository<NewsArticle>().Query()
                .Where(a => a.IsActive && a.Embedding != null && a.PublishedAt >= cutoffDate)
                .ToListAsync(cancellationToken);

            var modified = false;
            
            // Grupowanie według regionu, aby uniknąć scalania artykułów z różnych regionów
            var groups = activeArticles.GroupBy(a => a.Region);
            foreach (var group in groups)
            {
                var articles = group.ToList();
                for (int i = 0; i < articles.Count; i++)
                {
                    var primary = articles[i];
                    if (!primary.IsActive) continue; // Już scalony/deaktywowany

                    for (int j = i + 1; j < articles.Count; j++)
                    {
                        var secondary = articles[j];
                        if (!secondary.IsActive) continue;

                        if (primary.Embedding == null || secondary.Embedding == null)
                            continue;

                        var similarity = CalculateCosineSimilarity(primary.Embedding.ToArray(), secondary.Embedding.ToArray());
                        if (similarity >= 0.83) // Próg podobieństwa dla pełnych streszczeń (domyślnie 0.83)
                        {
                            _logger.LogInformation("[Post-Process] Merging article '{SecondaryTitle}' into '{PrimaryTitle}' due to similarity: {Similarity:F2}.", secondary.Title, primary.Title, similarity);
                            
                            var newUrls = new List<string>(primary.SourceUrls);
                            foreach (var url in secondary.SourceUrls)
                            {
                                if (!newUrls.Contains(url))
                                {
                                    newUrls.Add(url);
                                }
                            }
                            
                            primary.SourceUrls = newUrls;
                            secondary.IsActive = false;
                            
                            _uow.Repository<NewsArticle>().Update(primary);
                            _uow.Repository<NewsArticle>().Update(secondary);
                            modified = true;
                        }
                    }
                }
            }

            if (modified)
            {
                await _uow.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("[Post-Process] Successfully saved merged article updates to database.");
            }
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
