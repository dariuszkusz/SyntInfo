using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Models.Llm;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

public class SummarizeArticleCommandHandler
{
    private readonly IUnitOfWork _uow;
    private readonly IOpenRouterClient _openRouterClient;
    private readonly IGoogleAiStudioClient _googleAiStudioClient;
    private readonly ISearchService _searchService;
    private readonly IWebMetadataExtractor _webMetadataExtractor;
    private readonly ILogger<SummarizeArticleCommandHandler> _logger;
    private static readonly SemaphoreSlim _aiSemaphore = new SemaphoreSlim(1, 1);

    public SummarizeArticleCommandHandler(
        IUnitOfWork uow,
        IOpenRouterClient openRouterClient,
        IGoogleAiStudioClient googleAiStudioClient,
        ISearchService searchService,
        IWebMetadataExtractor webMetadataExtractor,
        ILogger<SummarizeArticleCommandHandler> logger)
    {
        _uow = uow;
        _openRouterClient = openRouterClient;
        _googleAiStudioClient = googleAiStudioClient;
        _searchService = searchService;
        _webMetadataExtractor = webMetadataExtractor;
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
            // 1. Wyszukanie kontekstu w sieci i wyciągnięcie faktów przez Analityka AI
            var searchResult = await _searchService.SearchDetailedWithImageAsync(command.Title, safeToken);
            var factsJson = await GenerateFactsAsync(command.Content, searchResult.Content, safeToken);

            if (!HasValidFacts(factsJson))
            {
                _logger.LogWarning("Odrzucono artykuł '{Title}' ze względu na brak poprawnych faktów wyekstrahowanych przez Analityka AI.", command.Title);
                return;
            }

            // 2. Wygenerowanie streszczenia przez Redaktora AI
            var (displayTitle, essence, categoryName) = await GenerateSummaryAsync(factsJson, command.Title, safeToken);

            if (!IsValidSummary(displayTitle, essence))
            {
                _logger.LogWarning("Odrzucono artykuł '{Title}' ze względu na nieprawidłowe streszczenie lub artefakt filtru bezpieczeństwa: '{Essence}'", command.Title, essence);
                return;
            }

            // 3. Rozstrzygnięcie zdjęcia oraz embeddingi wektorowe
            var imageUrl = await ResolveImageUrlAsync(command, searchResult.ImageUrl, safeToken);
            var embedding = await _openRouterClient.GenerateEmbeddingsAsync(essence, safeToken);

            // 4. Kategoria i utrwalenie w bazie (Create or Update)
            var category = await GetOrCreateCategoryAsync(categoryName, safeToken);
            await SaveOrUpdateArticleAsync(command, displayTitle, essence, factsJson, imageUrl, embedding, category, safeToken);

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

    private async Task<string> GenerateFactsAsync(string content, string searchResults, CancellationToken cancellationToken)
    {
        var factsJsonRaw = await _googleAiStudioClient.GenerateFactsAsync(content, searchResults, cancellationToken);
        return CleanJsonResponse(factsJsonRaw);
    }

    private async Task<(string Title, string Essence, string CategoryName)> GenerateSummaryAsync(
        string factsJson,
        string fallbackTitle,
        CancellationToken cancellationToken)
    {
        var editorResponseRaw = await _googleAiStudioClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
        var editorJson = CleanJsonResponse(editorResponseRaw);
        return ParseEditorResponse(editorJson, editorResponseRaw, fallbackTitle);
    }

    private (string Title, string Essence, string CategoryName) ParseEditorResponse(
        string editorJson,
        string editorResponseRaw,
        string fallbackTitle)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var structuredContent = JsonSerializer.Deserialize<InfopigulaContent>(editorJson, options);

            if (structuredContent != null && !string.IsNullOrWhiteSpace(structuredContent.Title) && !string.IsNullOrWhiteSpace(structuredContent.Essence))
            {
                return (
                    structuredContent.Title.Trim(),
                    structuredContent.Essence.Trim(),
                    structuredContent.Category?.Trim() ?? "General"
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd parsowania JSON z Redaktora. Raw: {Raw}", editorResponseRaw);
        }

        // Nigdy nie używamy surowego tekstu błędu lub moderacji jako treści podsumowania
        return (fallbackTitle, string.Empty, "General");
    }

    private static bool IsValidSummary(string title, string essence)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(essence))
        {
            return false;
        }

        // Prawdziwe podsumowanie newsa musi mieć minimalną długość (odrzucamy kilkunastoznakowe błędy i filtry)
        if (essence.Length < 40)
        {
            return false;
        }

        var lowerTitle = title.ToLowerInvariant();
        if (lowerTitle.Contains("brak faktów") ||
            lowerTitle.Contains("brak danych") ||
            lowerTitle.Contains("nie podano danych") ||
            lowerTitle.Contains("user safety"))
        {
            return false;
        }

        // Odrzucenie komunikatów filtrów moderacyjnych i technicznych artefaktów modeli LLM
        var lower = essence.ToLowerInvariant();
        if (lower.Contains("user safety") ||
            lower.Contains("safety filter") ||
            lower.Contains("as an ai") ||
            lower.Contains("as a language model") ||
            lower.Contains("i cannot fulfill") ||
            lower.Contains("i am sorry, but") ||
            lower.Contains("brak faktów") ||
            lower.Contains("brak danych") ||
            lower.Contains("nie podano danych") ||
            lower.Contains("brak wystarczających danych") ||
            lower.Contains("proszę dostarcz") ||
            lower.Contains("proszę o dostarczenie"))
        {
            return false;
        }

        return true;
    }

    private static bool HasValidFacts(string? factsJson)
    {
        if (string.IsNullOrWhiteSpace(factsJson))
        {
            return false;
        }

        var trimmed = factsJson.Trim();
        // Puste tablice JSON '[]' lub obiekty '{}' lub brak treści
        if (trimmed.Length < 10 || trimmed == "[]" || trimmed == "{}")
        {
            return false;
        }

        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("brak faktów") ||
            lower.Contains("brak danych") ||
            lower.Contains("user safety") ||
            lower.Contains("nie podano"))
        {
            return false;
        }

        return trimmed.StartsWith("[") || trimmed.StartsWith("{");
    }

    private async Task<string?> ResolveImageUrlAsync(
        SummarizeArticleCommand command,
        string? searchImageUrl,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.ImageUrl))
        {
            return command.ImageUrl;
        }

        if (!string.IsNullOrWhiteSpace(searchImageUrl))
        {
            return searchImageUrl;
        }

        if (!string.IsNullOrWhiteSpace(command.Url) && !command.Url.Contains("google.com/rss"))
        {
            return await _webMetadataExtractor.TryExtractOgImageAsync(command.Url, cancellationToken);
        }

        return null;
    }

    private async Task<NewsCategory> GetOrCreateCategoryAsync(string categoryName, CancellationToken cancellationToken)
    {
        var category = await _uow.Repository<NewsCategory>().Query()
            .FirstOrDefaultAsync(c => c.Name == categoryName, cancellationToken);

        if (category == null)
        {
            category = new NewsCategory { Name = categoryName };
            await _uow.Repository<NewsCategory>().AddAsync(category, cancellationToken);
        }

        return category;
    }

    private async Task SaveOrUpdateArticleAsync(
        SummarizeArticleCommand command,
        string displayTitle,
        string essence,
        string factsJson,
        string? resolvedImageUrl,
        float[] embedding,
        NewsCategory category,
        CancellationToken cancellationToken)
    {
        var urls = CombineSourceUrls(command.Url, command.AdditionalUrls);
        var vector = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null;

        NewsArticle? article = null;
        if (command.ExistingArticleId.HasValue)
        {
            article = await _uow.NewsArticles.GetByIdAsync(command.ExistingArticleId.Value, cancellationToken);
        }

        if (article != null)
        {
            article.Title = displayTitle;
            article.SummaryText = essence;
            article.SourceUrls = urls;
            article.Category = category;
            article.Embedding = vector;
            article.DeepContent = factsJson;
            article.IsActive = true;
            if (!string.IsNullOrWhiteSpace(resolvedImageUrl))
            {
                article.ImageUrl = resolvedImageUrl;
            }

            _uow.NewsArticles.Update(article);
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
                Embedding = vector,
                IsActive = true,
                DeepContent = factsJson,
                ImageUrl = resolvedImageUrl
            };
            await _uow.NewsArticles.AddAsync(article, cancellationToken);
            _logger.LogInformation("Zapisano nowy artykuł (OpenRouter Pipeline): {Title}", displayTitle);
        }
    }

    private static List<string> CombineSourceUrls(string primaryUrl, List<string>? additionalUrls)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryUrl };
        if (additionalUrls != null)
        {
            foreach (var url in additionalUrls)
            {
                urls.Add(url);
            }
        }
        return urls.ToList();
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
