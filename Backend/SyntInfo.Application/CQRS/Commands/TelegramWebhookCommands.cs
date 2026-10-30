namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda przetwarzająca akcję przycisku CallbackQuery odebraną z webhooka Telegrama.
/// </summary>
public record ProcessTelegramCallbackCommand(
    string CallbackQueryId,
    string? Data,
    string? Username,
    long? ChatId,
    long? MessageId);

/// <summary>
/// Komenda rejestrująca adres URL webhooka w API Telegrama.
/// </summary>
public record SetupTelegramWebhookCommand(string? WebhookUrl);

public record SetupTelegramWebhookResult(
    bool Success,
    string? Message = null,
    string? Error = null,
    bool IsValidationError = false);

