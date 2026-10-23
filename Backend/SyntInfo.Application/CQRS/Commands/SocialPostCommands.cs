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

/// <summary>
/// Komenda przełączająca wybór artykułu do wydania zbiorczego InfoSkrót.
/// </summary>
public record ToggleArticleDigestSelectionCommand(
    Guid ArticleId,
    long? TelegramChatId = null,
    long? TelegramMessageId = null);

/// <summary>
/// Komenda generująca i publikująca zbiorcze wydanie InfoSkrót z zaakceptowanych artykułów.
/// </summary>
public record PublishDigestIssueCommand(
    long? TelegramChatId = null,
    long? TelegramMessageId = null);

/// <summary>
/// Komenda wysyłająca testowy szkic posta na Telegram administratora do weryfikacji.
/// </summary>
public record SendTestSocialDraftCommand(SocialPlatformType Platform = SocialPlatformType.Wykop);

public record SendTestDraftResult(
    bool Success,
    Guid? DraftId,
    string? Platform,
    long TelegramMessageId,
    string Message,
    string? Error = null);

/// <summary>
/// Komenda wysyłająca partię artykułów do selekcji na Telegramie wraz z panelem sterowania wydaniem.
/// </summary>
public record SendCurationBatchCommand(int Count = 5);

public record SendCurationBatchResult(
    bool Success,
    int SentCount,
    string Message,
    string? Error = null);

