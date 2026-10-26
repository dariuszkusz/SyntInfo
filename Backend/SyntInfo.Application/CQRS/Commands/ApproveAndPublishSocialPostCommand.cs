namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda zatwierdzająca szkic posta i publikująca go na docelowej platformie.
/// </summary>
public record ApproveAndPublishSocialPostCommand(
    Guid DraftId,
    long? TelegramChatId = null,
    long? TelegramMessageId = null);
