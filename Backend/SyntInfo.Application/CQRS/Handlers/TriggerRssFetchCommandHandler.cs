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

namespace SyntInfo.Application.CQRS.Handlers
{
    public class TriggerRssFetchCommandHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly IMessageBus _bus;
        private readonly ILlmClient _llmClient;
        private readonly ILogger<TriggerRssFetchCommandHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public TriggerRssFetchCommandHandler(
            IUnitOfWork uow,
            IMessageBus bus,
            ILlmClient llmClient,
            ILogger<TriggerRssFetchCommandHandler> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _uow = uow;
            _bus = bus;
            _llmClient = llmClient;
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        [MessageTimeout(300)]
        public async Task Handle(TriggerRssFetchCommand _, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Rozpoczęto sprawdzanie feedów RSS.");

            // Dezaktywacja artykułów starszych niż 24h (naprawa wycieku statusu IsActive)
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

                var limitedOldArticles = oldActiveArticles.OrderByDescending(a => a.PublishedAt).Take(40).ToList();
                var limitedNewCandidates = newCandidates.OrderByDescending(c => c.PublishedAt).Take(100).ToList();

                foreach (var old in limitedOldArticles)
                {
                    var safeSummary = old.SummaryText ?? string.Empty;
                    var safeContent = safeSummary.Length > 100 ? safeSummary.Substring(0, 100) + "..." : safeSummary;
                    selectionList.Add(new { index = index++, title = old.Title, content = safeContent, type = "old", id = old.Id });
                }

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
    }
}
