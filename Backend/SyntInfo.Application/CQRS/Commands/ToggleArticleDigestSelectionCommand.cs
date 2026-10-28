namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda przełączająca wybór artykułu do wydania zbiorczego InfoSkrót.
/// </summary>
public record ToggleArticleDigestSelectionCommand(
    Guid ArticleId,
    long? TelegramChatId = null,
    long? TelegramMessageId = null,
    string? CallbackQueryId = null);
