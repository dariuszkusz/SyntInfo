using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

public static class PublishDigestIssueCommandHandler
{
    [MessageTimeout(300)] // 5 minut na syntezę i publikację wydania
    public static async Task Handle(
        PublishDigestIssueCommand command,
        IUnitOfWork uow,
        IMessageBus bus,
        ISocialPostGenerator postGenerator,
        IWykopClient wykopClient,
        IHejtoClient hejtoClient,
        IMailerLiteClient mailerLiteClient,
        ITelegramNotificationService telegramService,
        ITelegramCurationStateService telegramState,
        IOptions<CurationOptions> curationOptions,
        IOptions<HejtoOptions> hejtoOptions,
        IOptions<MailerLiteOptions> mailerLiteOptions,
        ILogger<PublishDigestIssueCommand> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Rozpoczęto generowanie i publikację zbiorczego wydania InfoSkrót.");

        var selectedArticles = await GetArticlesToPublishAsync(uow, curationOptions.Value.MaxArticlesPerRun, cancellationToken);
        if (!selectedArticles.Any())
        {
            logger.LogWarning("Brak aktywnych artykułów w bazie do publikacji wydania.");
            await telegramService.SendSimpleNotificationAsync(
                "⚠️ <b>Brak artykułów w bazie danych.</b>",
                cancellationToken);
            return;
        }

        await telegramService.SendSimpleNotificationAsync(
            $"⏳ <b>Trwa przygotowywanie wydania InfoSkrót z {selectedArticles.Count} artykułów...</b>\nGenerowanie treści i wysyłanie na platformy zewnętrzne.",
            cancellationToken);

        var wykopResult = await PublishWykopDigestAsync(selectedArticles, postGenerator, wykopClient, logger, cancellationToken);
        var hejtoResult = await PublishHejtoDigestAsync(selectedArticles, bus, postGenerator, hejtoClient, hejtoOptions.Value, logger, cancellationToken);
        var newsletterResult = await PublishNewsletterDigestAsync(selectedArticles, postGenerator, mailerLiteClient, mailerLiteOptions.Value, logger, cancellationToken);

        await MarkAndArchiveArticlesAsync(uow, selectedArticles, cancellationToken);
        telegramState.Clear();

        var summaryReport = $"🗞️ <b>Raport z publikacji wydania InfoSkrót ({selectedArticles.Count} artykułów)</b>\n\n" +
                            $"🟦 <b>Wykop:</b> {wykopResult}\n" +
                            $"🟩 <b>Hejto:</b> {hejtoResult}\n" +
                            $"📧 <b>Newsletter:</b> {newsletterResult}\n\n" +
                            $"#infoskrot";

        await telegramService.SendSimpleNotificationAsync(summaryReport, cancellationToken);
        logger.LogInformation("Zakończono publikację zbiorczego wydania InfoSkrót.");
    }

    private static async Task<List<NewsArticle>> GetArticlesToPublishAsync(
        IUnitOfWork uow,
        int maxArticlesPerRun,
        CancellationToken cancellationToken)
    {
        var selectedArticles = await uow.NewsArticles.GetSelectedForDigestAsync(cancellationToken);
        if (!selectedArticles.Any())
        {
            selectedArticles = await uow.NewsArticles.GetActiveArticlesAsync(maxArticlesPerRun, cancellationToken);
        }
        return selectedArticles;
    }

    private static async Task<string> PublishWykopDigestAsync(
        IReadOnlyList<NewsArticle> articles,
        ISocialPostGenerator postGenerator,
        IWykopClient wykopClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var wykopContent = await postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Wykop, articles, cancellationToken);
            var firstWithImage = articles.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.ImageUrl));
            string? photoKey = null;

            if (firstWithImage != null && !string.IsNullOrWhiteSpace(firstWithImage.ImageUrl))
            {
                try
                {
                    logger.LogInformation("Wysyłanie zdjęcia głównego wydania do Wykopu: {Url}", firstWithImage.ImageUrl);
                    photoKey = await wykopClient.UploadPhotoAsync(firstWithImage.ImageUrl, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Nie udało się przesłać zdjęcia głównego wydania do Wykopu. Kontynuacja bez zdjęcia.");
                }
            }

            var entryId = await wykopClient.CreateEntryAsync(wykopContent, photoKey, cancellationToken);
            return $"✅ Wpis dodany (ID: {entryId})";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas publikacji wydania na Wykopie");
            return $"❌ Błąd: {ex.Message}";
        }
    }

    private static async Task<string> PublishHejtoDigestAsync(
        IReadOnlyList<NewsArticle> articles,
        IMessageBus bus,
        ISocialPostGenerator postGenerator,
        IHejtoClient hejtoClient,
        HejtoOptions hejtoOptions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (hejtoOptions.PublishArticlesSeparately)
            {
                var articleIds = articles.Select(a => a.Id).ToList();
                var separateResult = await bus.InvokeAsync<PublishHejtoArticlesSeparatelyResult>(
                    new PublishHejtoArticlesSeparatelyCommand(ArticleIds: articleIds),
                    cancellationToken);

                return separateResult.Success
                    ? $"✅ Zaplanowano publikację {separateResult.PublishedCount}/{separateResult.TotalArticlesCount} wpisów w tle"
                    : $"❌ Błąd publikacji osobnej: {separateResult.Error ?? separateResult.Message}";
            }

            var hejtoContent = await postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Hejto, articles, cancellationToken);
            var postId = await hejtoClient.CreatePostAsync(hejtoContent, null, null, cancellationToken);
            return $"✅ Wpis dodany (ID: {postId})";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas publikacji wydania na Hejto");
            return $"❌ Błąd: {ex.Message}";
        }
    }

    private static async Task<string> PublishNewsletterDigestAsync(
        IReadOnlyList<NewsArticle> articles,
        ISocialPostGenerator postGenerator,
        IMailerLiteClient mailerLiteClient,
        MailerLiteOptions mailerLiteOptions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!mailerLiteOptions.Enabled)
        {
            logger.LogInformation("Wysyłka newslettera jest wyłączona w konfiguracji.");
            return "⏸️ Wyłączony";
        }

        try
        {
            var newsletterHtml = await postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Newsletter, articles, cancellationToken);
            var newsletterPlain = await postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Facebook, articles, cancellationToken);
            var issueTitle = $"InfoSkrót — Wydanie z {DateTime.UtcNow:dd.MM.yyyy}";
            await mailerLiteClient.SendNewsletterCampaignAsync(issueTitle, newsletterHtml, newsletterPlain, cancellationToken);
            return "✅ Kampania e-mail wysłana";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas wysyłania kampanii newslettera");
            return $"❌ Błąd: {ex.Message}";
        }
    }

    private static async Task MarkAndArchiveArticlesAsync(
        IUnitOfWork uow,
        IReadOnlyList<NewsArticle> selectedArticles,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var selectedIds = selectedArticles.Select(a => a.Id).ToHashSet();

        foreach (var article in selectedArticles)
        {
            article.DigestPublishedAt = nowUtc;
            article.IsSelectedForDigest = false;
            uow.NewsArticles.Update(article);
        }

        var unselectedArticles = await uow.NewsArticles.Query()
            .Where(a => a.IsActive && a.DigestPublishedAt == null && !selectedIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        foreach (var unselected in unselectedArticles)
        {
            unselected.DigestPublishedAt = nowUtc;
            unselected.IsSelectedForDigest = false;
            unselected.IsActive = false;
            uow.NewsArticles.Update(unselected);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }
}
