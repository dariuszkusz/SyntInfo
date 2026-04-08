using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CodeHollow.FeedReader;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine;
using Microsoft.Extensions.Logging;

namespace SyntInfo.Application.CQRS.Handlers
{
    public class RssCommandsHandler
    {
        private readonly IUnitOfWork _uow;
        private readonly IMessageBus _bus;
        private readonly ILlmClient _llmClient;
        private readonly ILogger<RssCommandsHandler> _logger;

        public RssCommandsHandler(
            IUnitOfWork uow, 
            IMessageBus bus, 
            ILlmClient llmClient,
            ILogger<RssCommandsHandler> logger)
        {
            _uow = uow;
            _bus = bus;
            _llmClient = llmClient;
            _logger = logger;
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
                    var feed = await FeedReader.ReadAsync(source.RssUrl, cancellationToken);
                    foreach (var item in feed.Items.Take(10)) // max 10 z jednego źródła do sprawdzenia żeby było szybko
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

            // Mechanizm "prostego kolejkowania" / paczkowania (max 5 na jedną rundę chroni LLM)
            var batchToProcess = newArticlesToProcess.OrderByDescending(a => a.PublishedAt).Take(5).ToList();
            
            _logger.LogInformation("Znaleziono {Count} nowych newsow. Przekazano {BatchCount} do LLM.", newArticlesToProcess.Count, batchToProcess.Count);

            foreach (var articleCmd in batchToProcess)
            {
                // Publikacja na szynę. Zostanie asynchronicznie chwycone przez Handle(SummarizeArticleCommand)
                await _bus.PublishAsync(articleCmd);
            }
        }

        public async Task Handle(SummarizeArticleCommand command, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Przetwarzanie (LLM) dla {Title}", command.Title);
            
            var summary = await _llmClient.GenerateSummaryAsync(command.Content, cancellationToken);
            if (string.IsNullOrWhiteSpace(summary))
                summary = "Nie udało się wygenerować streszczenia.";

            var embedding = await _llmClient.GenerateEmbeddingsAsync(summary, cancellationToken);

            var category = await _uow.Repository<NewsCategory>().Query()
                .FirstOrDefaultAsync(c => c.Name == "General", cancellationToken);
            
            if (category == null)
            {
                category = new NewsCategory { Name = "General" };
                await _uow.Repository<NewsCategory>().AddAsync(category, cancellationToken);
                await _uow.SaveChangesAsync(cancellationToken);
            }

            var article = new NewsArticle
            {
                Title = command.Title,
                SummaryText = summary,
                PublishedAt = command.PublishedAt,
                SourceUrls = new List<string> { command.Url },
                Region = command.Region,
                CategoryId = category.Id,
                Embedding = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null
            };

            await _uow.Repository<NewsArticle>().AddAsync(article, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Zapisano przetworzony artykuł: {Title}", command.Title);
        }
    }
}
