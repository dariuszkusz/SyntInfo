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

namespace SyntInfo.Application.CQRS.Handlers
{
    public class TriggerRssFetchCommandHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly IMessageBus _bus;
        private readonly IOpenRouterClient _openRouterClient;
        private readonly ILogger<TriggerRssFetchCommandHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public TriggerRssFetchCommandHandler(
            IUnitOfWork uow,
            IMessageBus bus,
            IOpenRouterClient openRouterClient,
            ILogger<TriggerRssFetchCommandHandler> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _uow = uow;
            _bus = bus;
            _openRouterClient = openRouterClient;
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

            var allCommands = new List<SummarizeArticleCommand>();
            allCommands.AddRange(await GetCommandsToExecuteAsync(SourceRegion.World, newArticlesToProcess.Where(a => a.Region == SourceRegion.World).ToList(), maxPerRegion, cancellationToken));
            allCommands.AddRange(await GetCommandsToExecuteAsync(SourceRegion.Poland, newArticlesToProcess.Where(a => a.Region == SourceRegion.Poland).ToList(), maxPerRegion, cancellationToken));

            if (allCommands.Any())
            {

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

                var selectedIndexes = await _openRouterClient.SelectTopArticlesIndexesAsync(prompt, maxPerRegion, cancellationToken);

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
    }
}
