using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Application.CQRS.Handlers;

public static class TelegramWebhookCommandHandler
{
    public static async Task Handle(
        ProcessTelegramCallbackCommand command,
        IMessageBus bus,
        ITelegramNotificationService telegramService,
        ILogger<ProcessTelegramCallbackCommand> logger,
        CancellationToken cancellationToken)
    {
        var data = command.Data ?? string.Empty;
        logger.LogInformation("Odebrano CallbackQuery z Telegrama: Data='{Data}', From='{User}'", data, command.Username);

        var parts = data.Split(':', 2);
        var action = parts[0].ToLowerInvariant();

        // Odpowiedz botowi na Telegramie, aby zdjąć stan ładowania przycisku (z wyjątkiem curate_toggle, gdzie odpowiedź wysyła dedykowany handler)
        if (action != "curate_toggle" && !string.IsNullOrWhiteSpace(command.CallbackQueryId))
        {
            await telegramService.AnswerCallbackQueryAsync(
                command.CallbackQueryId,
                "Przetwarzanie decyzji...",
                showAlert: false,
                cancellationToken: cancellationToken);
        }

        if (action == "digest_publish")
        {
            logger.LogInformation("Wyzwalanie publikacji wydania InfoSkrót z poziomu Telegrama.");
            await bus.PublishAsync(new PublishDigestIssueCommand(command.ChatId, command.MessageId));
        }
        else if (action == "curate_toggle" && parts.Length == 2 && Guid.TryParse(parts[1], out var articleId))
        {
            logger.LogInformation("Przełączanie selekcji artykułu {ArticleId} do wydania.", articleId);
            await bus.PublishAsync(new ToggleArticleDigestSelectionCommand(articleId, command.ChatId, command.MessageId, command.CallbackQueryId));
        }
        else if (parts.Length == 2 && Guid.TryParse(parts[1], out var draftId))
        {
            if (action == "approve")
            {
                logger.LogInformation("Wyzwalanie publikacji posta dla szkicu {DraftId}", draftId);
                await bus.PublishAsync(new ApproveAndPublishSocialPostCommand(draftId, command.ChatId, command.MessageId));
            }
            else if (action == "reject")
            {
                logger.LogInformation("Wyzwalanie odrzucenia szkicu {DraftId}", draftId);
                await bus.PublishAsync(new RejectSocialPostDraftCommand(draftId, command.ChatId, command.MessageId));
            }
        }
    }

    public static async Task<SetupTelegramWebhookResult> Handle(
        SetupTelegramWebhookCommand command,
        ITelegramNotificationService telegramService,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<SetupTelegramWebhookCommand> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.WebhookUrl))
        {
            return new SetupTelegramWebhookResult(
                Success: false,
                Error: "Brak podanego adresu webhookUrl w ciele żądania lub query.",
                IsValidationError: true);
        }

        var opts = telegramOptions.Value;
        if (string.IsNullOrWhiteSpace(opts.BotToken) || opts.BotToken.Contains("PLACEHOLDER"))
        {
            return new SetupTelegramWebhookResult(
                Success: false,
                Error: "Brak skonfigurowanego Telegram:BotToken.",
                IsValidationError: true);
        }

        var rawSecret = opts.SecretToken;
        string? secretToken = null;
        if (!string.IsNullOrWhiteSpace(rawSecret) && !rawSecret.Contains("PLACEHOLDER"))
        {
            // Telegram dopuszcza wyłącznie znaki [A-Za-z0-9_-]
            secretToken = new string(rawSecret.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        }

        try
        {
            await telegramService.SetWebhookAsync(command.WebhookUrl, secretToken, cancellationToken);
            logger.LogInformation("Webhook zarejestrowany w Telegramie: {Url}", command.WebhookUrl);
            return new SetupTelegramWebhookResult(
                Success: true,
                Message: $"Webhook zarejestrowany na adres {command.WebhookUrl}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas rejestracji webhooka w Telegram API");
            return new SetupTelegramWebhookResult(Success: false, Error: ex.Message);
        }
    }
}

