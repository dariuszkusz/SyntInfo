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
                // KROK 0: Pobranie kontekstu z Tavily
                var searchResults = await _searchService.SearchDetailedInfoAsync(command.Title, safeToken);

                // KROK 1: Analityk (OpenRouter) - Fakty JSON -> DeepContent
                var factsJsonRaw = await _openRouterClient.GenerateFactsAsync(command.Content, searchResults, safeToken);
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
                        displayTitle = structuredContent.Title;
                        essence = structuredContent.Essence;
                        categoryName = structuredContent.Category ?? "General";
                    }
                    else
                    {
                        essence = editorJson.Length > 4500 ? editorJson.Substring(0, 4500) : editorJson;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Błąd parsowania JSON z Redaktora. Raw: {Raw}", editorResponseRaw);
                    essence = editorJson.Length > 4500 ? editorJson.Substring(0, 4500) : editorJson;
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

                var article = new NewsArticle
                {
                    Title = displayTitle,
                    OriginalTitle = command.Title,
                    SummaryText = essence,
                    PublishedAt = command.PublishedAt,
                    SourceUrls = new List<string> { command.Url },
                    Region = command.Region,
                    Category = category,
                    Embedding = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null,
                    IsActive = true,
                    DeepContent = factsJson // Zapisujemy ustrukturyzowane fakty z Kroku 1
                };

                await _uow.Repository<NewsArticle>().AddAsync(article, safeToken);
                await _uow.SaveChangesAsync(safeToken); // Wykonuje wszystko (kategoria + artykuł) w jednej transakcji
                _logger.LogInformation("Zapisano przetworzony artykuł (OpenRouter Pipeline): {Title}", displayTitle);
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
    }
}
