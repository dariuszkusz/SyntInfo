using SyntInfo.Domain.Entities;

namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda generująca szkice postów na platformy społecznościowe dla najnowszych artykułów
/// i wysyłająca je do administratora przez Telegram do akceptacji (human-in-the-loop).
/// </summary>
public record CreateSocialPostDraftsCommand(
    Guid? SpecificArticleId = null,
    int? LimitArticles = null);

/// <summary>
/// Komenda zatwierdzająca szkic posta i publikująca go na docelowej platformie.
/// </summary>
public record ApproveAndPublishSocialPostCommand(
    Guid DraftId,
    long? TelegramChatId = null,
    long? TelegramMessageId = null);

/// <summary>
/// Komenda odrzucająca szkic posta po kliknięciu przycisku Odrzuć na Telegramie.
/// </summary>
public record RejectSocialPostDraftCommand(
    Guid DraftId,
    long? TelegramChatId = null,
    long? TelegramMessageId = null);
