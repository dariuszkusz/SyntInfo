using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Llm;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class SummarizeArticleCommandHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly IOpenRouterClient _openRouterClient;
        private readonly IGoogleAiStudioClient _googleAiStudioClient;
        private readonly ISearchService _searchService;
        private readonly ILogger<SummarizeArticleCommandHandler> _logger;
        private static readonly SemaphoreSlim _aiSemaphore = new SemaphoreSlim(1, 1);

        public SummarizeArticleCommandHandler(
            IUnitOfWork uow,
            IOpenRouterClient openRouterClient,
            IGoogleAiStudioClient googleAiStudioClient,
            ISearchService searchService,
            ILogger<SummarizeArticleCommandHandler> logger)
        {
            _uow = uow;
            _openRouterClient = openRouterClient;
            _googleAiStudioClient = googleAiStudioClient;
            _searchService = searchService;
            _logger = logger;
        }

        [MessageTimeout(900)] // 15 minut na jeden artykuł (ważne dla modeli reasoning)
        public async Task Handle(SummarizeArticleCommand command, CancellationToken cancellationToken)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var safeToken = cts.Token;

            await _aiSemaphore.WaitAsync(safeToken);
            try
            {
                // KROK 0: Pobranie kontekstu z Tavily (wraz z ewentualnym zdjęciem)
                var searchResult = await _searchService.SearchDetailedWithImageAsync(command.Title, safeToken);
                var searchResults = searchResult.Content;

                // KROK 1: Analityk (Google AI Studio z fallbackiem na OpenRouter) - Fakty JSON -> DeepContent
                var factsJsonRaw = await _googleAiStudioClient.GenerateFactsAsync(command.Content, searchResults, safeToken);
                var factsJson = CleanJsonResponse(factsJsonRaw);

                // KROK 2: Redaktor (Google AI Studio z fallbackiem na OpenRouter) - Minimalistyczne podsumowanie JSON (Tytuł, Esencja, Kategoria)
                var editorResponseRaw = await _googleAiStudioClient.GenerateSummaryFromFactsAsync(factsJson, safeToken);
                var editorJson = CleanJsonResponse(editorResponseRaw);

                string displayTitle = command.Title;
                string essence = "Nie udało się wygenerować streszczenia.";
                string categoryName = "General";

                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var structuredContent = JsonSerializer.Deserialize<InfopigulaContent>(editorJson, options);

                    if (structuredContent != null && !string.IsNullOrWhiteSpace(structuredContent.Title))
                    {
                        displayTitle = structuredContent.Title.Trim();
                        essence = structuredContent.Essence?.Trim() ?? string.Empty;
                        categoryName = structuredContent.Category?.Trim() ?? "General";
                    }
                    else
                    {
                        essence = editorJson.Length > 4500 ? editorJson.Substring(0, 4500) : editorJson;
                        essence = essence.Trim();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Błąd parsowania JSON z Redaktora. Raw: {Raw}", editorResponseRaw);
                    essence = editorJson.Length > 4500 ? editorJson.Substring(0, 4500) : editorJson;
                    essence = essence.Trim();
                }

                // Rozstrzygnięcie zdjęcia dla artykułu: RSS -> Tavily -> og:image z artykułu źródłowego
                string? resolvedImageUrl = !string.IsNullOrWhiteSpace(command.ImageUrl) ? command.ImageUrl : searchResult.ImageUrl;
                if (string.IsNullOrWhiteSpace(resolvedImageUrl) && !string.IsNullOrWhiteSpace(command.Url) && !command.Url.Contains("google.com/rss"))
                {
                    resolvedImageUrl = await TryExtractOgImageAsync(command.Url, safeToken);
                }

                // KROK 3: Wektory (pgvector) na podstawie GOTOWEGO streszczenia (OpenRouter)
                var embedding = await _openRouterClient.GenerateEmbeddingsAsync(essence, safeToken);

                // Zapis do bazy (Unit of Work - pojedyncza transakcja)
                var category = await _uow.Repository<NewsCategory>().Query()
                    .FirstOrDefaultAsync(c => c.Name == categoryName, safeToken);

                if (category == null)
                {
                    category = new NewsCategory { Name = categoryName };
                    await _uow.Repository<NewsCategory>().AddAsync(category, safeToken);
                    // Nie wywołujemy SaveChangesAsync tutaj, EF obsłuży to w jednej transakcji
                }

                var urls = new List<string> { command.Url };
                if (command.AdditionalUrls != null)
                {
                    foreach (var u in command.AdditionalUrls)
                    {
                        if (!urls.Contains(u))
                            urls.Add(u);
                    }
                }

                NewsArticle? article = null;
                if (command.ExistingArticleId.HasValue)
                {
                    article = await _uow.Repository<NewsArticle>().Query()
                        .FirstOrDefaultAsync(a => a.Id == command.ExistingArticleId.Value, safeToken);
                }

                if (article != null)
                {
                    article.Title = displayTitle;
                    article.SummaryText = essence;
                    article.SourceUrls = urls;
                    article.Category = category;
                    article.Embedding = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null;
                    article.DeepContent = factsJson;
                    article.IsActive = true;
                    if (!string.IsNullOrWhiteSpace(resolvedImageUrl))
                    {
                        article.ImageUrl = resolvedImageUrl;
                    }
                    
                    _uow.Repository<NewsArticle>().Update(article);
                    _logger.LogInformation("Zaktualizowano istniejący artykuł po klastrowaniu semantycznym: {Title}", displayTitle);
                }
                else
                {
                    article = new NewsArticle
                    {
                        Title = displayTitle,
                        OriginalTitle = command.Title.Trim(),
                        SummaryText = essence,
                        PublishedAt = command.PublishedAt,
                        SourceUrls = urls,
                        Region = command.Region,
                        Category = category,
                        Embedding = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null,
                        IsActive = true,
                        DeepContent = factsJson,
                        ImageUrl = resolvedImageUrl
                    };
                    await _uow.Repository<NewsArticle>().AddAsync(article, safeToken);
                    _logger.LogInformation("Zapisano nowy artykuł (OpenRouter Pipeline): {Title}", displayTitle);
                }

                await _uow.SaveChangesAsync(safeToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas przetwarzania artykułu: {Title}", command.Title);
            }
            finally
            {
                _aiSemaphore.Release();
            }
        }

        private string CleanJsonResponse(string raw)
        {
            var cleaned = raw.Trim();
            if (cleaned.Contains("```"))
            {
                int firstCodeBlock = cleaned.IndexOf("```");
                int lastCodeBlock = cleaned.LastIndexOf("```");

                if (firstCodeBlock != lastCodeBlock && firstCodeBlock >= 0)
                {
                    var sub = cleaned.Substring(firstCodeBlock + 3, lastCodeBlock - firstCodeBlock - 3).Trim();
                    if (sub.StartsWith("json")) sub = sub.Substring(4).Trim();
                    cleaned = sub;
                }
            }

            if (cleaned.Contains("\" +"))
            {
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\""\s*\+\s*\n?\s*\""", "");
            }

            var firstBrace = cleaned.IndexOf('{');
            var firstBracket = cleaned.IndexOf('[');
            int start = -1;

            if (firstBrace >= 0 && (firstBracket < 0 || firstBrace < firstBracket)) start = firstBrace;
            else if (firstBracket >= 0) start = firstBracket;

            if (start >= 0)
            {
                char startChar = cleaned[start];
                char endChar = startChar == '{' ? '}' : ']';
                int depth = 0;
                int end = -1;
                for (int i = start; i < cleaned.Length; i++)
                {
                    if (cleaned[i] == startChar) depth++;
                    else if (cleaned[i] == endChar)
                    {
                        depth--;
                        if (depth == 0) { end = i; break; }
                    }
                }
                if (end > start)
                    cleaned = cleaned.Substring(start, end - start + 1);
            }

            return cleaned;
        }

        private static async Task<string?> TryExtractOgImageAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                // Odczytaj nagłówek HTML (pierwsze 64 KB)
                char[] buffer = new char[65536];
                int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0) return null;

                var htmlHead = new string(buffer, 0, read);
                var match = System.Text.RegularExpressions.Regex.Match(htmlHead, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    match = System.Text.RegularExpressions.Regex.Match(htmlHead, @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                if (!match.Success)
                {
                    match = System.Text.RegularExpressions.Regex.Match(htmlHead, @"<meta[^>]+name=[""']twitter:image[""'][^>]+content=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }

                if (match.Success)
                {
                    var imgUrl = match.Groups[1].Value;
                    if (imgUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        return imgUrl;
                    }
                }
            }
            catch
            {
                // Ignoruj błędy przy próbie pobrania og:image
            }

            return null;
        }
    }
}
