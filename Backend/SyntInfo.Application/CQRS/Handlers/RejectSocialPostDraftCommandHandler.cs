using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.Application.CQRS.Handlers;

public static class RejectSocialPostDraftCommandHandler
{
    public static async Task Handle(
        RejectSocialPostDraftCommand command,
        IUnitOfWork uow,
        ITelegramNotificationService telegramService,
        ILogger<RejectSocialPostDraftCommand> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Odrzucanie szkicu posta {DraftId}", command.DraftId);

        var draft = await uow.Repository<SocialPostDraft>().Query()
            .FirstOrDefaultAsync(d => d.Id == command.DraftId, cancellationToken);

        if (draft == null)
        {
            logger.LogWarning("Nie znaleziono szkicu posta o ID {DraftId}", command.DraftId);
            return;
        }

        if (!draft.CanBeRejected)
        {
            logger.LogWarning("Szkic posta {DraftId} ma status {Status} i nie może zostać odrzucony.", draft.Id, draft.Status);
            return;
        }

        draft.Reject();
        uow.Repository<SocialPostDraft>().Update(draft);
        await uow.SaveChangesAsync(cancellationToken);

        var telegramMsgId = command.TelegramMessageId ?? draft.TelegramMessageId;
        if (telegramMsgId.HasValue)
        {
            await telegramService.UpdateDraftMessageAsync(
                telegramMsgId.Value,
                draft,
                "🚫 Odrzucono publikację",
                cancellationToken);
        }
    }
}
