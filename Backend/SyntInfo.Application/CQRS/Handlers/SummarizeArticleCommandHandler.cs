using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Commands;
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
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(15));
        var safeToken = cts.Token;

        await _aiSemaphore.WaitAsync(safeToken);
        try
        {
            // 1. Wyszukanie kontekstu w sieci i wyciągnięcie faktów przez Analityka AI
            var searchResult = await _searchService.SearchDetailedWithImageAsync(command.Title, safeToken);
            var factsJson = await GenerateFactsAsync(command.Content, searchResult.Content, safeToken);

            if (!AiSummaryResponseParser.HasValidFacts(factsJson))
            {
                _logger.LogWarning("Odrzucono artykuł '{Title}' ze względu na brak poprawnych faktów wyekstrahowanych przez Analityka AI.", command.Title);
                return;
            }

            // 2. Wygenerowanie streszczenia przez Redaktora AI
            var (displayTitle, essence, categoryName, rawTags) = await GenerateSummaryAsync(factsJson, command.Title, safeToken);

            if (!AiSummaryResponseParser.IsValidSummary(displayTitle, essence))
            {
                _logger.LogWarning("Odrzucono artykuł '{Title}' ze względu na nieprawidłowe streszczenie lub artefakt filtru bezpieczeństwa: '{Essence}'", command.Title, essence);
                return;
            }

            // 3. Rozstrzygnięcie zdjęcia oraz embeddingi wektorowe
            var imageUrl = await ResolveImageUrlAsync(command, searchResult.ImageUrl, safeToken);
            var embedding = await _openRouterClient.GenerateEmbeddingsAsync(essence, safeToken);

            // 4. Kategoria, tagi i utrwalenie w bazie (Create or Update)
            var sanitizedTags = TagFormattingHelper.SanitizeAndFilterTags(rawTags, command.Region);
            var category = await GetOrCreateCategoryAsync(categoryName, safeToken);
            await SaveOrUpdateArticleAsync(command, displayTitle, essence, factsJson, imageUrl, embedding, category, sanitizedTags, safeToken);

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
        return AiSummaryResponseParser.CleanJsonResponse(factsJsonRaw);
    }

    private async Task<(string Title, string Essence, string CategoryName, List<string> Tags)> GenerateSummaryAsync(
        string factsJson,
        string fallbackTitle,
        CancellationToken cancellationToken)
    {
        var editorResponseRaw = await _googleAiStudioClient.GenerateSummaryFromFactsAsync(factsJson, cancellationToken);
        var editorJson = AiSummaryResponseParser.CleanJsonResponse(editorResponseRaw);
        return AiSummaryResponseParser.ParseEditorResponse(editorJson, editorResponseRaw, fallbackTitle, _logger);
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
        List<string> tags,
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
            article.Tags = tags;
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
                Tags = tags,
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
}
