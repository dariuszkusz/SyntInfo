using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SyntInfo.Infrastructure.Services;

public class TelegramNotificationService : ITelegramNotificationService
{
    private readonly ITelegramBotClient? _botClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramNotificationService> _logger;

    public TelegramNotificationService(
        IConfiguration configuration,
        ILogger<TelegramNotificationService> logger,
        ITelegramBotClient? botClient = null)
    {
        _configuration = configuration;
        _logger = logger;
        _botClient = botClient;
    }

    private ITelegramBotClient? ResolveClient()
    {
        if (_botClient != null) return _botClient;

        var token = _configuration["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(token) || token.Contains("PLACEHOLDER"))
        {
            return null;
        }

        return new TelegramBotClient(token);
    }

    public async Task<long> SendDraftToAdminAsync(SocialPostDraft draft, NewsArticle article, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _configuration["Telegram:AdminChatId"];
        var isMock = _configuration.GetValue("Telegram:UseMock", false);
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || chatIdStr.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("TelegramNotificationService [MOCK/TEST]: Nowy szkic [{Platform}] dla artykułu '{Title}' (DraftId: {DraftId})",
                draft.PlatformType, article.Title, draft.Id);
            return Random.Shared.Next(1000, 99999);
        }

        if (!long.TryParse(chatIdStr, out var chatId))
        {
            _logger.LogWarning("Niepoprawny format Telegram:AdminChatId: {Value}", chatIdStr);
            return 0;
        }

        var platformIcon = draft.PlatformType switch
        {
            SocialPlatformType.Wykop => "🟦 Wykop v3",
            SocialPlatformType.Hejto => "🟩 Hejto",
            SocialPlatformType.Facebook => "🔷 Facebook",
            SocialPlatformType.Newsletter => "📧 Newsletter",
            _ => draft.PlatformType.ToString()
        };

        var encodedTitle = WebUtility.HtmlEncode(article.Title);
        var encodedContent = WebUtility.HtmlEncode(draft.Content);

        var messageText = $"<b>[{platformIcon}] Propozycja wpisu</b>\n" +
                          $"<b>Artykuł:</b> {encodedTitle}\n" +
                          $"<b>ID:</b> <code>{draft.Id}</code>\n\n" +
                          $"{encodedContent}";

        if (messageText.Length > 4000)
        {
            messageText = messageText[..3950] + "\n\n<i>[...skrócono treść...]</i>";
        }

        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Opublikuj", $"approve:{draft.Id}"),
                InlineKeyboardButton.WithCallbackData("❌ Odrzuć", $"reject:{draft.Id}")
            }
        });

        try
        {
            var sentMessage = await client.SendMessage(
                chatId: new ChatId(chatId),
                text: messageText,
                parseMode: ParseMode.Html,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Wysłano szkic {DraftId} na Telegram (MessageId: {MessageId})", draft.Id, sentMessage.Id);
            return sentMessage.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas wysyłania wiadomości Telegram do chatu {ChatId}", chatId);
            throw;
        }
    }

    public async Task UpdateDraftMessageAsync(long messageId, SocialPostDraft draft, string decisionSummary, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _configuration["Telegram:AdminChatId"];
        var isMock = _configuration.GetValue("Telegram:UseMock", false);
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || chatIdStr.Contains("PLACEHOLDER"))
        {
            _logger.LogInformation("TelegramNotificationService [MOCK/TEST]: Aktualizacja wiadomości {MessageId}: {Summary}", messageId, decisionSummary);
            return;
        }

        if (!long.TryParse(chatIdStr, out var chatId))
        {
            return;
        }

        try
        {
            var encodedContent = WebUtility.HtmlEncode(draft.Content);
            var updatedText = $"<b>[{draft.PlatformType}] {decisionSummary}</b>\n" +
                              $"<b>ID:</b> <code>{draft.Id}</code>\n" +
                              $"<b>Data:</b> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n\n" +
                              $"{encodedContent}";

            if (updatedText.Length > 4000)
            {
                updatedText = updatedText[..3950] + "\n\n<i>[...skrócono treść...]</i>";
            }

            // Edytujemy treść i usuwamy przyciski akcji
            await client.EditMessageText(
                chatId: new ChatId(chatId),
                messageId: (int)messageId,
                text: updatedText,
                parseMode: ParseMode.Html,
                replyMarkup: null,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Zaktualizowano wiadomość {MessageId} w Telegramie ze statusem: {Summary}", messageId, decisionSummary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się zaktualizować wiadomości {MessageId} na Telegramie: {Error}", messageId, ex.Message);
        }
    }
}
