using Microsoft.EntityFrameworkCore;
using CodeHollow.FeedReader;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class RssCommandsHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly IMessageBus _bus;
        private readonly ILlmClient _llmClient;
        private readonly ISearchService _searchService;
        private readonly ILogger<RssCommandsHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private static readonly SemaphoreSlim _aiSemaphore = new SemaphoreSlim(1, 1);

        public RssCommandsHandler(
            IUnitOfWork uow,
            IMessageBus bus,
            ILlmClient llmClient,
            ISearchService searchService,
            ILogger<RssCommandsHandler> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _uow = uow;
            _bus = bus;
            _llmClient = llmClient;
            _searchService = searchService;
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        [MessageTimeout(300)]
        public async Task Handle(TriggerRssFetchCommand _, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Rozpoczeto sprawdzanie feedow RSS.");
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
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    var response = await client.GetAsync(source.RssUrl, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            _logger.LogWarning("Źródło RSS nie zostało znalezione (404): {SourceUrl}", source.RssUrl);
                        else
                            _logger.LogWarning("Błąd podczas pobierania RSS {StatusCode}: {SourceUrl}", response.StatusCode, source.RssUrl);
                        continue;
                    }

                    var rssContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    var feed = FeedReader.ReadFromString(rssContent);

                    foreach (var item in feed.Items.Take(20)) // Pobieramy z każdego źródła nowości bez sztucznych limitów
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
                    _logger.LogError(ex, "Wystapil blad podczas pobierania feedu z {SourceUrl}", source.RssUrl);
                }
            }

            await _uow.SaveChangesAsync(cancellationToken);

            var maxPerRegion = _configuration.GetValue("ProcessingSettings:MaxArticlesPerRegion", 10);
            await ProcessRegionSelectionAsync(SourceRegion.World, newArticlesToProcess.Where(a => a.Region == SourceRegion.World).ToList(), maxPerRegion, cancellationToken);
            await ProcessRegionSelectionAsync(SourceRegion.Poland, newArticlesToProcess.Where(a => a.Region == SourceRegion.Poland).ToList(), maxPerRegion, cancellationToken);

        }

        private async Task ProcessRegionSelectionAsync(SourceRegion region, List<SummarizeArticleCommand> newCandidates, int maxPerRegion, CancellationToken cancellationToken)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddHours(-24);
                var oldActiveArticles = await _uow.Repository<NewsArticle>().Query()
                    .Where(a => a.Region == region && a.IsActive && a.PublishedAt >= cutoffDate)
                    .ToListAsync(cancellationToken);
                if (newCandidates == null || !newCandidates.Any())
                {
                    return;
                }

                var selectionList = new List<object>();
                int index = 0;

                // Zabezpieczenie przed przepełnieniem okna LLM - bierzemy max 40 starych i 100 nowych z czołówki
                var limitedOldArticles = oldActiveArticles.OrderByDescending(a => a.PublishedAt).Take(40).ToList();
                var limitedNewCandidates = newCandidates.OrderByDescending(c => c.PublishedAt).Take(100).ToList();

                // Mapowanie starych (jeszcze aktywnych, max z 24h)
                foreach (var old in limitedOldArticles)
                {
                    var safeSummary = old.SummaryText ?? string.Empty;
                    var safeContent = safeSummary.Length > 100 ? safeSummary.Substring(0, 100) + "..." : safeSummary;
                    selectionList.Add(new { index = index++, title = old.Title, content = safeContent, type = "old", id = old.Id });
                }
                // Mapowanie nowych
                var newStartIndex = index;
                foreach (var cand in limitedNewCandidates)
                {
                    var safeCandContent = cand.Content ?? string.Empty;
                    var safeContent = safeCandContent.Length > 100 ? safeCandContent.Substring(0, 100) + "..." : safeCandContent;
                    selectionList.Add(new { index = index++, title = cand.Title, content = safeContent, type = "new" });
                }

                if (selectionList.Count <= maxPerRegion)
                {
                    foreach (var cand in limitedNewCandidates)
                    {
                        await _bus.InvokeAsync(cand, cancellationToken);
                    }
                    return;
                }

                var jsonStr = JsonSerializer.Serialize(selectionList);
                var selectedIndexes = await _llmClient.SelectTopArticlesIndexesAsync(jsonStr, maxPerRegion, cancellationToken);

                if (selectedIndexes == null || !selectedIndexes.Any())
                {
                    foreach (var cand in limitedNewCandidates.Take(maxPerRegion)) await _bus.PublishAsync(cand);
                    return;
                }

                // Przetwarzanie i rotacja
                var selectedOldIds = new HashSet<Guid>();
                foreach (var i in selectedIndexes)
                {
                    if (i < newStartIndex && i >= 0 && i < selectionList.Count)
                    {
                        var idObj = ((dynamic)selectionList[i]).id;
                        if (idObj != null) selectedOldIds.Add(idObj);
                    }
                    else if (i >= newStartIndex && i < selectionList.Count)
                    {
                        var cand = limitedNewCandidates[i - newStartIndex];
                        await _bus.InvokeAsync(cand, cancellationToken);
                    }
                }

                // Dezaktywacja starych artykułów, które nie dostały się do Top 10
                var toDeactivate = oldActiveArticles.Where(a => !selectedOldIds.Contains(a.Id)).ToList();

                foreach (var a in toDeactivate)
                {
                    a.IsActive = false;
                    _uow.Repository<NewsArticle>().Update(a);
                }

                if (toDeactivate.Any())
                {
                    await _uow.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd krytyczny w ProcessRegionSelectionAsync dla regionu {Region}", region);
                throw;
            }
        }

        public async Task Handle(SummarizeArticleCommand command, CancellationToken cancellationToken)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var safeToken = cts.Token;

            await _aiSemaphore.WaitAsync(safeToken);
            try
            {
                var searchResults = await _searchService.SearchDetailedInfoAsync(command.Title, safeToken);
                var aiResponseRaw = await _llmClient.GenerateEnrichedSummaryAsync(command.Content, searchResults, safeToken);

                var cleanedResponse = aiResponseRaw.Trim();
                if (cleanedResponse.Contains("```"))
                {
                    int firstCodeBlock = cleanedResponse.IndexOf("```");
                    int lastCodeBlock = cleanedResponse.LastIndexOf("```");

                    if (firstCodeBlock != lastCodeBlock && firstCodeBlock >= 0)
                    {
                        var sub = cleanedResponse.Substring(firstCodeBlock + 3, lastCodeBlock - firstCodeBlock - 3).Trim();
                        if (sub.StartsWith("json")) sub = sub.Substring(4).Trim();
                        cleanedResponse = sub;
                    }
                }

                var aiResponseJson = cleanedResponse;

                if (aiResponseJson.Contains("\" +"))
                {
                    aiResponseJson = System.Text.RegularExpressions.Regex.Replace(aiResponseJson, @"\""\s*\+\s*\n?\s*\""", "");
                }

                var firstBrace = aiResponseJson.IndexOf('{');
                if (firstBrace >= 0)
                {
                    int depth = 0;
                    int endBrace = -1;
                    for (int i = firstBrace; i < aiResponseJson.Length; i++)
                    {
                        if (aiResponseJson[i] == '{') depth++;
                        else if (aiResponseJson[i] == '}')
                        {
                            depth--;
                            if (depth == 0) { endBrace = i; break; }
                        }
                    }
                    if (endBrace > firstBrace)
                        aiResponseJson = aiResponseJson.Substring(firstBrace, endBrace - firstBrace + 1);
                }

                string displayTitle = command.Title;
                string essence = "Nie udało się wygenerować streszczenia.";
                string categoryName = "General";

                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var structuredContent = JsonSerializer.Deserialize<SyntInfo.Application.Models.Llm.InfopigulaContent>(aiResponseJson, options);

                    if (structuredContent != null && !string.IsNullOrWhiteSpace(structuredContent.Title))
                    {
                        displayTitle = structuredContent.Title;
                        essence = structuredContent.Essence;
                        categoryName = structuredContent.Category ?? "General";
                    }
                    else
                    {
                        essence = aiResponseJson.Length > 4500 ? aiResponseJson.Substring(0, 4500) : aiResponseJson;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Błąd parsowania JSON z LLM. Raw: {Raw}", aiResponseRaw);
                    essence = aiResponseJson.Length > 4500 ? aiResponseJson.Substring(0, 4500) : aiResponseJson;
                }

                var embedding = await _llmClient.GenerateEmbeddingsAsync(essence, safeToken);

                var category = await _uow.Repository<NewsCategory>().Query()
                    .FirstOrDefaultAsync(c => c.Name == categoryName, safeToken);

                if (category == null)
                {
                    category = new NewsCategory { Name = categoryName };
                    await _uow.Repository<NewsCategory>().AddAsync(category, safeToken);
                    await _uow.SaveChangesAsync(safeToken);
                }

                var article = new NewsArticle
                {
                    Title = displayTitle,
                    OriginalTitle = command.Title,
                    SummaryText = essence,
                    PublishedAt = command.PublishedAt,
                    SourceUrls = new List<string> { command.Url },
                    Region = command.Region,
                    CategoryId = category.Id,
                    Embedding = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null,
                    IsActive = true, // Od razu ustawiane jako aktywne
                    DeepContent = searchResults // Zapisujemy wyniki Deep Search
                };

                await _uow.Repository<NewsArticle>().AddAsync(article, safeToken);
                await _uow.SaveChangesAsync(safeToken);

                _logger.LogInformation("Zapisano przetworzony artykuł: {Title}", command.Title);
            }
            finally
            {
                _aiSemaphore.Release();
            }
        }

        public async Task Handle(ClearAllArticlesCommand command, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Rozpoczęto czyszczenie bazy danych z wiadomości.");
            await _uow.Repository<NewsArticle>().Query().ExecuteDeleteAsync(cancellationToken);
            var sources = await _uow.Repository<NewsSource>().Query().ToListAsync(cancellationToken);
            foreach (var source in sources)
            {
                source.LastFetchedAt = DateTime.MinValue;
                _uow.Repository<NewsSource>().Update(source);
            }
            await _uow.SaveChangesAsync(cancellationToken);
        }
    }
}
