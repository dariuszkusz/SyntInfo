namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda generująca i publikująca zbiorcze wydanie InfoSkrót z zaakceptowanych artykułów.
/// </summary>
public record PublishDigestIssueCommand(
    long? TelegramChatId = null,
    long? TelegramMessageId = null);
