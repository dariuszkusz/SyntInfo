using Microsoft.EntityFrameworkCore;
using CodeHollow.FeedReader;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class RssCommandsHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly IMessageBus _bus;
        private readonly ILlmClient _llmClient;
        private readonly ILogger<RssCommandsHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private static readonly SemaphoreSlim _aiSemaphore = new SemaphoreSlim(1, 1);

        public RssCommandsHandler(
            IUnitOfWork uow,
            IMessageBus bus,
            ILlmClient llmClient,
            ILogger<RssCommandsHandler> logger,
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

        public async Task Handle(TriggerRssFetchCommand command, CancellationToken cancellationToken)
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
                    // Używamy HttpClient z User-Agent, aby uniknąć blokowania przez niektóre serwisy (np. Reuters)
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

                    foreach (var item in feed.Items.Take(10))
                    {
                        var url = item.Link;
                        // Sprawdzenie czy w naszej bazie istnieje juz ten url we wczesniej przetworzonych linkach
                        var exists = await _uow.Repository<NewsArticle>().Query()
                            .AnyAsync(a => a.SourceUrls.Contains(url) || a.Title == item.Title, cancellationToken);

                        if (!exists)
                        {
                            var content = !string.IsNullOrWhiteSpace(item.Description) ? item.Description : item.Content;
                            // Czasami feedy dają null, wezmy Title jezeli brak tresci.
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

            // Pobieramy limit z konfiguracji (domyślnie 5 jeśli brak zapisu)
            var maxPerRegion = _configuration.GetValue<int>("ProcessingSettings:MaxArticlesPerRegion", 5);

            // Mechanizm "prostego kolejkowania" / paczkowania - osobno dla regionów
            var polandBatch = newArticlesToProcess
                .Where(a => a.Region == SourceRegion.Poland)
                .OrderByDescending(a => a.PublishedAt)
                .Take(maxPerRegion)
                .ToList();

            var worldBatch = newArticlesToProcess
                .Where(a => a.Region == SourceRegion.World)
                .OrderByDescending(a => a.PublishedAt)
                .Take(maxPerRegion)
                .ToList();

            var batchToProcess = polandBatch.Concat(worldBatch).ToList();

            _logger.LogInformation("Znaleziono {Count} nowych newsow. Przekazano {BatchCount} do LLM (PL: {PlCount}, World: {WorldCount}). Luimit per region: {Limit}",
                newArticlesToProcess.Count, batchToProcess.Count, polandBatch.Count, worldBatch.Count, maxPerRegion);

            foreach (var articleCmd in batchToProcess)
            {
                // Publikacja na szynę. Zostanie asynchronicznie chwycone przez Handle(SummarizeArticleCommand)
                await _bus.PublishAsync(articleCmd);
            }
        }

        public async Task Handle(SummarizeArticleCommand command, CancellationToken cancellationToken)
        {
            // Tworzymy bezpieczny token (10 minut), ignorując krótki timeout Wolverine
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var safeToken = cts.Token;

            await _aiSemaphore.WaitAsync(safeToken);
            try
            {
                _logger.LogInformation("Przetwarzanie (LLM) dla {Title}", command.Title);

                var aiResponseRaw = await _llmClient.GenerateSummaryAsync(command.Content, safeToken);
                
                // Agresywne czyszczenie odpowiedzi LLM z bloków markdown ```json lub ```
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

                // Naprawa typowych błędów LLM: konkatenacja stringów za pomocą '+'
                // Przykład: "text" + \n " more text" -> "text more text"
                if (aiResponseJson.Contains("\" +"))
                {
                    // Prosta próba naprawy przez usunięcie wzorca: " + (opcjonalny whitespace) "
                    aiResponseJson = System.Text.RegularExpressions.Regex.Replace(aiResponseJson, @"\""\s*\+\s*\n?\s*\""", "");
                    _logger.LogInformation("Naprawiono konkatenację stringów w JSON.");
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
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var structuredContent = System.Text.Json.JsonSerializer.Deserialize<SyntInfo.Application.Models.Llm.InfopigulaContent>(aiResponseJson, options);

                    if (structuredContent != null && !string.IsNullOrWhiteSpace(structuredContent.Title))
                    {
                        displayTitle = structuredContent.Title;
                        essence = structuredContent.Essence;
                        categoryName = structuredContent.Category ?? "General";
                    }
                    else
                    {
                        // Fallback – używamy wyciągniętego JSONa lub oczyszczonej odpowiedzi, jeśli nie udało się zdeserializować poprawnie
                        essence = aiResponseJson.Length > 4500 ? aiResponseJson.Substring(0, 4500) : aiResponseJson;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Błąd parsowania JSON z LLM. Użycie danych surowych (oczyszczonych). Raw: {Raw}", aiResponseRaw);
                    // Fallback – używamy wyciągniętego JSONa jako tekstu
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
                    Embedding = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null
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

            // Usuwamy wszystkie artykuły
            await _uow.Repository<NewsArticle>().Query().ExecuteDeleteAsync(cancellationToken);

            // Resetujemy datę ostatniego pobrania dla wszystkich źródeł, aby mogły pobrać wszystko na nowo
            var sources = await _uow.Repository<NewsSource>().Query().ToListAsync(cancellationToken);
            foreach (var source in sources)
            {
                source.LastFetchedAt = DateTime.MinValue;
                _uow.Repository<NewsSource>().Update(source);
            }

            await _uow.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Baza danych została wyczyszczona z wiadomości.");
        }
    }
}
