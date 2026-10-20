using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

public static class CreateSocialPostDraftsCommandHandler
{
    [MessageTimeout(600)] // 10 minut na wygenerowanie i wysłanie szkiców
    public static async Task Handle(
        CreateSocialPostDraftsCommand command,
        IUnitOfWork uow,
        ISocialPostGenerator postGenerator,
        ITelegramNotificationService telegramService,
        IConfiguration configuration,
        ILogger<CreateSocialPostDraftsCommand> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Rozpoczęto generowanie szkiców postów dla social media.");

        var defaultLimit = configuration.GetValue("SocialMedia:MaxArticlesPerRun", 3);
        var limit = command.LimitArticles ?? defaultLimit;

        List<NewsArticle> articlesToProcess;

        if (command.SpecificArticleId.HasValue)
        {
            var article = await uow.Repository<NewsArticle>().Query()
                .Include(a => a.Category)
                .FirstOrDefaultAsync(a => a.Id == command.SpecificArticleId.Value, cancellationToken);

            articlesToProcess = article != null ? new List<NewsArticle> { article } : new List<NewsArticle>();
        }
        else
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var recentArticles = await uow.Repository<NewsArticle>().Query()
                .Include(a => a.Category)
                .Where(a => a.IsActive && a.PublishedAt >= cutoff)
                .OrderByDescending(a => a.PublishedAt)
                .Take(20)
                .ToListAsync(cancellationToken);

            var existingDraftArticleIds = await uow.Repository<SocialPostDraft>().Query()
                .Where(d => d.CreatedAt >= cutoff)
                .Select(d => d.NewsArticleId)
                .Distinct()
                .ToListAsync(cancellationToken);

            articlesToProcess = recentArticles
                .Where(a => !existingDraftArticleIds.Contains(a.Id))
                .Take(limit)
                .ToList();
        }

        if (!articlesToProcess.Any())
        {
            logger.LogInformation("Brak nowych artykułów do przygotowania szkiców postów.");
            return;
        }

        logger.LogInformation("Generowanie szkiców dla {Count} artykułów.", articlesToProcess.Count);

        var platforms = new[]
        {
            SocialPlatformType.Wykop,
            SocialPlatformType.Hejto,
            SocialPlatformType.Facebook,
            SocialPlatformType.Newsletter
        };

        var articleIds = articlesToProcess.Select(a => a.Id).ToList();
        var existingDrafts = await uow.Repository<SocialPostDraft>().Query()
            .Where(d => articleIds.Contains(d.NewsArticleId))
            .Select(d => new { d.NewsArticleId, d.PlatformType })
            .ToListAsync(cancellationToken);

        var existingDraftKeys = new HashSet<(Guid ArticleId, SocialPlatformType Platform)>(
            existingDrafts.Select(d => (d.NewsArticleId, d.PlatformType)));

        var newDraftsCount = 0;

        foreach (var article in articlesToProcess)
        {
            foreach (var platform in platforms)
            {
                try
                {
                    if (existingDraftKeys.Contains((article.Id, platform)))
                    {
                        continue;
                    }

                    var content = await postGenerator.GenerateContentForPlatformAsync(platform, article, cancellationToken);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        logger.LogWarning("Pusta treść wygenerowana dla platformy {Platform}, artykuł: {Title}", platform, article.Title);
                        continue;
                    }

                    var draft = new SocialPostDraft
                    {
                        NewsArticleId = article.Id,
                        PlatformType = platform,
                        Content = content,
                        Status = SocialPostStatus.Draft,
                        CreatedAt = DateTime.UtcNow
                    };

                    try
                    {
                        var telegramMsgId = await telegramService.SendDraftToAdminAsync(draft, article, cancellationToken);
                        if (telegramMsgId > 0)
                        {
                            draft.TelegramMessageId = telegramMsgId;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Nie udało się wysłać szkicu na Telegram dla platformy {Platform}: {Message}", platform, ex.Message);
                    }

                    await uow.Repository<SocialPostDraft>().AddAsync(draft, cancellationToken);
                    existingDraftKeys.Add((article.Id, platform));
                    newDraftsCount++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Błąd podczas tworzenia szkicu dla platformy {Platform}, artykuł {ArticleId}", platform, article.Id);
                }
            }
        }

        if (newDraftsCount > 0)
        {
            await uow.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Zapisano {Count} nowych szkiców w bazie danych.", newDraftsCount);
        }

        logger.LogInformation("Zakończono generowanie szkiców social media.");
    }
}
