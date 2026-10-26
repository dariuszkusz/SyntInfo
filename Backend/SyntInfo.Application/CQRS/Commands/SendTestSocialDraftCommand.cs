namespace SyntInfo.Application.CQRS.Commands;

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
