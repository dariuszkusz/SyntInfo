using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

public static class PublishDigestIssueCommandHandler
{
    [MessageTimeout(300)] // 5 minut na syntezę i publikację wydania
    public static async Task Handle(
        PublishDigestIssueCommand command,
        IUnitOfWork uow,
        ISocialPostGenerator postGenerator,
        IWykopClient wykopClient,
        IHejtoClient hejtoClient,
        IMailerLiteClient mailerLiteClient,
        ITelegramNotificationService telegramService,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILogger<PublishDigestIssueCommand> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Rozpoczęto generowanie i publikację zbiorczego wydania InfoSkrót.");

        var selectedArticles = await uow.Repository<NewsArticle>().Query()
            .Where(a => a.IsSelectedForDigest && a.DigestPublishedAt == null)
            .OrderByDescending(a => a.PublishedAt)
            .ToListAsync(cancellationToken);

        if (!selectedArticles.Any())
        {
            selectedArticles = await uow.Repository<NewsArticle>().Query()
                .Where(a => a.IsActive)
                .OrderByDescending(a => a.PublishedAt)
                .Take(4)
                .ToListAsync(cancellationToken);
        }

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

        string? wykopResult = null;
        string? hejtoResult = null;
        string? newsletterResult = null;

        // 1. Publikacja na Wykop v3
        try
        {
            var wykopContent = await postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Wykop, selectedArticles, cancellationToken);
            var firstWithImage = selectedArticles.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.ImageUrl));
            string? photoKey = null;

            if (firstWithImage != null && !string.IsNullOrWhiteSpace(firstWithImage.ImageUrl))
            {
                logger.LogInformation("Wysyłanie zdjęcia głównego wydania do Wykopu: {Url}", firstWithImage.ImageUrl);
                photoKey = await wykopClient.UploadPhotoAsync(firstWithImage.ImageUrl, cancellationToken);
            }

            var entryId = await wykopClient.CreateEntryAsync(wykopContent, photoKey, cancellationToken);
            wykopResult = $"✅ Wpis dodany (ID: {entryId})";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas publikacji wydania na Wykopie");
            wykopResult = $"❌ Błąd: {ex.Message}";
        }

        // 2. Publikacja na Hejto
        try
        {
            var hejtoContent = await postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Hejto, selectedArticles, cancellationToken);
            var postId = await hejtoClient.CreatePostAsync(hejtoContent, null, cancellationToken);
            hejtoResult = $"✅ Wpis dodany (ID: {postId})";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas publikacji wydania na Hejto");
            hejtoResult = $"❌ Błąd: {ex.Message}";
        }

        // 3. Publikacja Newslettera (MailerLite)
        var isNewsletterEnabled = bool.TryParse(configuration["SocialPlatforms:MailerLite:Enabled"], out var isEnabled) && isEnabled;
        if (isNewsletterEnabled)
        {
            try
            {
                var newsletterHtml = await postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Newsletter, selectedArticles, cancellationToken);
                var newsletterPlain = await postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Facebook, selectedArticles, cancellationToken);
                var issueTitle = $"InfoSkrót — Wydanie z {DateTime.UtcNow:dd.MM.yyyy}";
                await mailerLiteClient.SendNewsletterCampaignAsync(issueTitle, newsletterHtml, newsletterPlain, cancellationToken);
                newsletterResult = "✅ Kampania e-mail wysłana";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Błąd podczas wysyłania kampanii newslettera");
                newsletterResult = $"❌ Błąd: {ex.Message}";
            }
        }
        else
        {
            logger.LogInformation("Wysyłka newslettera jest wyłączona w konfiguracji.");
            newsletterResult = "⏸️ Wyłączony";
        }

        // 4. Oznaczenie artykułów jako opublikowane w wydaniu
        foreach (var article in selectedArticles)
        {
            article.DigestPublishedAt = DateTime.UtcNow;
            article.IsSelectedForDigest = false;
            uow.Repository<NewsArticle>().Update(article);
        }
        await uow.SaveChangesAsync(cancellationToken);

        // 5. Raport końcowy na Telegramie
        var summaryReport = $"🗞️ <b>Raport z publikacji wydania InfoSkrót ({selectedArticles.Count} artykułów)</b>\n\n" +
                            $"🟦 <b>Wykop:</b> {wykopResult}\n" +
                            $"🟩 <b>Hejto:</b> {hejtoResult}\n" +
                            $"📧 <b>Newsletter:</b> {newsletterResult}\n\n" +
                            $"#infoskrot";

        await telegramService.SendSimpleNotificationAsync(summaryReport, cancellationToken);
        logger.LogInformation("Zakończono publikację zbiorczego wydania InfoSkrót.");
    }
}
