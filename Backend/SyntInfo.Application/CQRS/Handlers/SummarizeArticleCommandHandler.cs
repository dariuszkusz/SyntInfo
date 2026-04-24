using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Llm;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class SummarizeArticleCommandHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly ILlmClient _llmClient;
        private readonly ISearchService _searchService;
        private readonly ILogger<SummarizeArticleCommandHandler> _logger;
        private static readonly SemaphoreSlim _aiSemaphore = new SemaphoreSlim(1, 1);

        public SummarizeArticleCommandHandler(
            IUnitOfWork uow,
            ILlmClient llmClient,
            ISearchService searchService,
            ILogger<SummarizeArticleCommandHandler> logger)
        {
            _uow = uow;
            _llmClient = llmClient;
            _searchService = searchService;
            _logger = logger;
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
                    var structuredContent = JsonSerializer.Deserialize<InfopigulaContent>(aiResponseJson, options);

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
                    IsActive = true,
                    DeepContent = searchResults
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
    }
}
