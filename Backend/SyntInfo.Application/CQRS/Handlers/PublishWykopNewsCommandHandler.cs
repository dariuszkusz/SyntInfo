using SyntInfo.Application.CQRS.Commands;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

/// <summary>
/// Handler CQRS pobierający wiadomości z bazy danych i publikujący je jako wpis na mikroblogu Wykop.
/// </summary>
public static class PublishWykopNewsCommandHandler
{
    [MessageTimeout(180)]
    public static async Task<PublishWykopNewsResult> Handle(
        PublishWykopNewsCommand command,
        IUnitOfWork uow,
        ISocialPostGenerator postGenerator,
        IWykopClient wykopClient,
        ITelegramNotificationService telegramService,
        IOptions<CurationOptions> curationOptions,
        ILogger<PublishWykopNewsCommand> logger,
        CancellationToken cancellationToken)
    {
        var count = command.Count is > 0 ? command.Count.Value : curationOptions.Value.MaxArticlesPerRun;
        logger.LogInformation("Rozpoczęto publikację {Count} wiadomości na Wykopie.", count);

        var articles = await uow.NewsArticles.GetActiveArticlesAsync(count, cancellationToken);

        if (articles.Count == 0)
        {
            logger.LogWarning("Brak aktywnych artykułów w bazie danych do opublikowania na Wykopie.");
            return new PublishWykopNewsResult(
                Success: false,
                PublishedArticlesCount: 0,
                EntryId: null,
                Message: "Brak aktywnych artykułów w bazie danych do opublikowania.",
                Error: "Brak artykułów.");
        }

        try
        {
            var wykopContent = await postGenerator.GenerateDigestContentForPlatformAsync(
                SocialPlatformType.Wykop,
                articles,
                cancellationToken);

            var firstWithImage = articles.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.ImageUrl));
            string? photoKey = null;

            if (firstWithImage != null && !string.IsNullOrWhiteSpace(firstWithImage.ImageUrl))
            {
                try
                {
                    logger.LogInformation("Wysyłanie zdjęcia głównego do Wykopu: {Url}", firstWithImage.ImageUrl);
                    photoKey = await wykopClient.UploadPhotoAsync(firstWithImage.ImageUrl, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Nie udało się przesłać zdjęcia do Wykopu, kontynuuję publikację bez zdjęcia.");
                }
            }

            var entryId = await wykopClient.CreateEntryAsync(wykopContent, photoKey, cancellationToken);
            logger.LogInformation("Pomyślnie opublikowano wpis na Wykopie o ID: {EntryId}", entryId);

            foreach (var article in articles)
            {
                article.DigestPublishedAt = DateTime.UtcNow;
                uow.NewsArticles.Update(article);
            }
            await uow.SaveChangesAsync(cancellationToken);

            try
            {
                await telegramService.SendSimpleNotificationAsync(
                    $"📢 <b>Opublikowano {articles.Count} wiadomości na Wykopie!</b>\n" +
                    $"ID wpisu: <code>{entryId}</code>\n#wykop #syntinfo",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Nie udało się wysłać powiadomienia na Telegram o publikacji na Wykopie.");
            }

            return new PublishWykopNewsResult(
                Success: true,
                PublishedArticlesCount: articles.Count,
                EntryId: entryId,
                Message: $"Pomyślnie opublikowano {articles.Count} wiadomości na Wykopie (ID: {entryId}).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas publikacji wiadomości na Wykopie.");
            return new PublishWykopNewsResult(
                Success: false,
                PublishedArticlesCount: 0,
                EntryId: null,
                Message: "Wystąpił błąd podczas publikacji wiadomości na Wykopie.",
                Error: ex.Message);
        }
    }
}
