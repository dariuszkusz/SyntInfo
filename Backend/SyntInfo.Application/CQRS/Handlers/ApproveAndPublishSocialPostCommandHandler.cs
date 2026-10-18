using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

public static class ApproveAndPublishSocialPostCommandHandler
{
    [MessageTimeout(180)] // 3 minuty na publikację
    public static async Task Handle(
        ApproveAndPublishSocialPostCommand command,
        IUnitOfWork uow,
        ISocialPublisherFactory publisherFactory,
        ITelegramNotificationService telegramService,
        ILogger<ApproveAndPublishSocialPostCommand> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Zatwierdzanie i publikowanie szkicu posta {DraftId}", command.DraftId);

        var draft = await uow.Repository<SocialPostDraft>().Query()
            .Include(d => d.NewsArticle)
            .FirstOrDefaultAsync(d => d.Id == command.DraftId, cancellationToken);

        if (draft == null)
        {
            logger.LogWarning("Nie znaleziono szkicu posta o ID {DraftId}", command.DraftId);
            return;
        }

        if (draft.NewsArticle == null)
        {
            logger.LogError("Szkic posta {DraftId} nie ma powiązanego artykułu NewsArticle", command.DraftId);
            return;
        }

        if (!draft.CanBeApproved)
        {
            logger.LogWarning("Szkic posta {DraftId} ma status {Status} i nie może zostać zatwierdzony.", draft.Id, draft.Status);
            return;
        }

        draft.Approve();
        uow.Repository<SocialPostDraft>().Update(draft);
        await uow.SaveChangesAsync(cancellationToken);

        var publisher = publisherFactory.GetPublisher(draft.PlatformType);
        if (publisher == null)
        {
            var errorMsg = $"Brak zarejestrowanego modułu publikacji dla platformy {draft.PlatformType}.";
            logger.LogError(errorMsg);
            draft.MarkFailed(errorMsg);
            uow.Repository<SocialPostDraft>().Update(draft);
            await uow.SaveChangesAsync(cancellationToken);

            var telegramMsgIdToNotify = command.TelegramMessageId ?? draft.TelegramMessageId;
            if (telegramMsgIdToNotify.HasValue)
            {
                await telegramService.UpdateDraftMessageAsync(telegramMsgIdToNotify.Value, draft, "❌ Błąd konfiguracji platformy", cancellationToken);
            }
            return;
        }

        try
        {
            var publishResult = await publisher.PublishAsync(draft, draft.NewsArticle, cancellationToken);

            if (publishResult.Success)
            {
                draft.MarkPublished(publishResult.ExternalPostId, DateTime.UtcNow);
                logger.LogInformation("Pomyślnie opublikowano post {DraftId} na platformie {Platform}. External ID: {ExternalId}",
                    draft.Id, draft.PlatformType, publishResult.ExternalPostId);

                uow.Repository<SocialPostDraft>().Update(draft);
                await uow.SaveChangesAsync(cancellationToken);

                var telegramMsgId = command.TelegramMessageId ?? draft.TelegramMessageId;
                if (telegramMsgId.HasValue)
                {
                    await telegramService.UpdateDraftMessageAsync(
                        telegramMsgId.Value,
                        draft,
                        $"✅ Opublikowano na {draft.PlatformType}" + (!string.IsNullOrEmpty(publishResult.ExternalPostId) ? $" (ID: {publishResult.ExternalPostId})" : string.Empty),
                        cancellationToken);
                }
            }
            else
            {
                draft.MarkFailed(publishResult.ErrorMessage ?? "Nieznany błąd publikacji");
                logger.LogWarning("Nieudana publikacja posta {DraftId} na platformie {Platform}: {Error}",
                    draft.Id, draft.PlatformType, publishResult.ErrorMessage);

                uow.Repository<SocialPostDraft>().Update(draft);
                await uow.SaveChangesAsync(cancellationToken);

                var telegramMsgId = command.TelegramMessageId ?? draft.TelegramMessageId;
                if (telegramMsgId.HasValue)
                {
                    await telegramService.UpdateDraftMessageAsync(
                        telegramMsgId.Value,
                        draft,
                        $"⚠️ Błąd publikacji: {publishResult.ErrorMessage}",
                        cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Wystąpił wyjątek podczas publikacji posta {DraftId} na platformie {Platform}", draft.Id, draft.PlatformType);
            draft.MarkFailed($"Wyjątek: {ex.Message}");
            uow.Repository<SocialPostDraft>().Update(draft);
            await uow.SaveChangesAsync(cancellationToken);

            var telegramMsgId = command.TelegramMessageId ?? draft.TelegramMessageId;
            if (telegramMsgId.HasValue)
            {
                await telegramService.UpdateDraftMessageAsync(
                    telegramMsgId.Value,
                    draft,
                    $"💥 Błąd krytyczny: {ex.Message}",
                    cancellationToken);
            }
        }
    }
}
