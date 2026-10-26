namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda odrzucająca szkic posta po kliknięciu przycisku Odrzuć na Telegramie.
/// </summary>
public record RejectSocialPostDraftCommand(
    Guid DraftId,
    long? TelegramChatId = null,
    long? TelegramMessageId = null);
