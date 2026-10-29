using System.Net;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SyntInfo.Infrastructure.Services;

public class TelegramNotificationService : ITelegramNotificationService
{
    private readonly ITelegramBotClient? _botClient;
    private readonly TelegramOptions _telegramOptions;
    private readonly CurationOptions _curationOptions;
    private readonly ILogger<TelegramNotificationService> _logger;

    public TelegramNotificationService(
        IOptions<TelegramOptions> telegramOptions,
        ILogger<TelegramNotificationService> logger,
        IOptions<CurationOptions>? curationOptions = null,
        ITelegramBotClient? botClient = null)
    {
        _telegramOptions = telegramOptions.Value;
        _curationOptions = curationOptions?.Value ?? new CurationOptions();
        _logger = logger;
        _botClient = botClient;
    }

    private ITelegramBotClient? ResolveClient()
    {
        if (_botClient != null) return _botClient;

        var token = _telegramOptions.BotToken;
        if (string.IsNullOrWhiteSpace(token) || token.Contains("PLACEHOLDER"))
        {
            return null;
        }

        return new TelegramBotClient(token);
    }

    public async Task<long> SendDraftToAdminAsync(SocialPostDraft draft, NewsArticle article, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
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
            var sentMessage = await ExecuteWithRetryAsync(() => client.SendMessage(
                chatId: new ChatId(chatId),
                text: messageText,
                parseMode: ParseMode.Html,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken), cancellationToken);

            _logger.LogInformation("Wysłano szkic {DraftId} na Telegram (MessageId: {MessageId})", draft.Id, sentMessage.Id);
            return sentMessage.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas wysyłania wiadomości Telegram do chatu {ChatId}", chatId);
            throw;
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.ErrorCode == 429)
            {
                var delaySeconds = ex.Parameters?.RetryAfter ?? (attempt * 2);
                _logger.LogWarning("Telegram API zwróciło 429 Too Many Requests (Rate Limit). Oczekiwanie {Seconds}s przed ponowieniem próby {Attempt}/{MaxRetries}",
                    delaySeconds, attempt, maxRetries);

                if (attempt == maxRetries) throw;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        return await action();
    }

    public async Task UpdateDraftMessageAsync(long messageId, SocialPostDraft draft, string decisionSummary, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
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

    public async Task<long> SendArticleForCurationAsync(NewsArticle article, bool isSelected, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || chatIdStr.Contains("PLACEHOLDER") || !long.TryParse(chatIdStr, out var chatId))
        {
            _logger.LogInformation("TelegramNotificationService [MOCK/TEST]: Preselekcja artykułu '{Title}' (Selected: {IsSelected})", article.Title, isSelected);
            return Random.Shared.Next(1000, 99999);
        }

        var statusIcon = isSelected ? "✅ WYBRANO DO WYDANIA" : "⏳ CZEKA NA DECYZJĘ";
        var btnText = isSelected ? "➖ Pomiń / Usuń z wydania" : "➕ Wybierz do wydania InfoSkrót";
        var encodedTitle = WebUtility.HtmlEncode(article.Title);
        var encodedSummary = WebUtility.HtmlEncode(article.SummaryText);
        var primaryUrl = article.SourceUrls.FirstOrDefault() ?? "https://infoskrot.pl";

        var text = $"<b>[{statusIcon}]</b>\n\n" +
                   $"<b>📌 {encodedTitle}</b>\n\n" +
                   $"{encodedSummary}\n\n" +
                   $"🔗 <a href=\"{primaryUrl}\">Czytaj źródło</a>\n" +
                   $"🆔 <code>{article.Id}</code>";

        if (text.Length > 4000) text = text[..3950] + "...";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(btnText, $"curate_toggle:{article.Id}") }
        });

        try
        {
            var msg = await ExecuteWithRetryAsync(() => client.SendMessage(
                chatId: new ChatId(chatId),
                text: text,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken), cancellationToken);

            return msg.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania artykułu do preselekcji na Telegram");
            return 0;
        }
    }

    public async Task UpdateCurationMessageAsync(long messageId, NewsArticle article, bool isSelected, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            return;
        }

        var statusIcon = isSelected ? "✅ WYBRANO DO WYDANIA" : "⏳ CZEKA NA DECYZJĘ";
        var btnText = isSelected ? "➖ Usuń z wydania" : "➕ Wybierz do wydania InfoSkrót";
        var encodedTitle = WebUtility.HtmlEncode(article.Title);
        var encodedSummary = WebUtility.HtmlEncode(article.SummaryText);
        var primaryUrl = article.SourceUrls.FirstOrDefault() ?? "https://infoskrot.pl";

        var text = $"<b>[{statusIcon}]</b>\n\n" +
                   $"<b>📌 {encodedTitle}</b>\n\n" +
                   $"{encodedSummary}\n\n" +
                   $"🔗 <a href=\"{primaryUrl}\">Czytaj źródło</a>\n" +
                   $"🆔 <code>{article.Id}</code>";

        if (text.Length > 4000) text = text[..3950] + "...";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(btnText, $"curate_toggle:{article.Id}") }
        });

        try
        {
            await client.EditMessageText(
                chatId: new ChatId(chatId),
                messageId: (int)messageId,
                text: text,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się zaktualizować kafelka artykułu {MessageId}", messageId);
        }
    }

    public async Task<long> SendDigestControlPanelAsync(int selectedCount, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            return Random.Shared.Next(1000, 99999);
        }

        var text = $"🚀 <b>Panel Wydania InfoSkrót</b>\n\n" +
                   $"Aktualnie zaakceptowanych artykułów do wydania: <b>{selectedCount}</b>\n\n" +
                   $"Po kliknięciu przycisku poniżej system połączy wybrane artykuły w <b>jedno spójne wydanie</b> z grafikami i opublikuje na Wykop, Hejto oraz Newsletter!";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🚀 Opublikuj wydanie InfoSkrót", "digest_publish") }
        });

        try
        {
            var msg = await ExecuteWithRetryAsync(() => client.SendMessage(
                chatId: new ChatId(chatId),
                text: text,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken), cancellationToken);

            return msg.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania panelu wydania na Telegram");
            return 0;
        }
    }

    public async Task<long> SendDigestControlPanelAsync(int polandCount, int worldCount, int positiveCount, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            return Random.Shared.Next(1000, 99999);
        }

        var text = BuildControlPanelText(polandCount, worldCount, positiveCount);
        var keyboard = BuildControlPanelMarkup();

        try
        {
            var msg = await ExecuteWithRetryAsync(() => client.SendMessage(
                chatId: new ChatId(chatId),
                text: text,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken), cancellationToken);

            return msg.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania panelu wydania na Telegram");
            return 0;
        }
    }

    public async Task UpdateDigestControlPanelAsync(long messageId, int polandCount, int worldCount, int positiveCount, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            return;
        }

        var text = BuildControlPanelText(polandCount, worldCount, positiveCount);
        var keyboard = BuildControlPanelMarkup();

        try
        {
            await client.EditMessageText(
                chatId: new ChatId(chatId),
                messageId: (int)messageId,
                text: text,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się zaktualizować panelu kontrolnego {MessageId}", messageId);
        }
    }

    public async Task<long> SendThematicBatchAsync(SourceRegion region, IReadOnlyList<NewsArticle> articles, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            _logger.LogInformation("TelegramNotificationService [MOCK/TEST]: Wysłano zbiorczą partię dla regionu {Region} ({Count} artykułów)", region, articles.Count);
            return Random.Shared.Next(1000, 99999);
        }

        var maxLimit = _curationOptions.GetMaxSelectionLimit(region);
        var header = region switch
        {
            SourceRegion.Poland => $"🇵🇱 <b>WYDANIE DNIA — POLSKA</b> (Wybierz maks. {maxLimit})",
            SourceRegion.World => $"🌍 <b>WYDANIE DNIA — ŚWIAT</b> (Wybierz maks. {maxLimit})",
            SourceRegion.Positive => $"☀️ <b>WYDANIE DNIA — POZYTYWNE</b> (Wybierz maks. {maxLimit})",
            _ => "🗞️ <b>PROPOZYCJE ARTYKUŁÓW</b>"
        };

        var sb = new StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━");

        for (var i = 0; i < articles.Count; i++)
        {
            var art = articles[i];
            var num = i + 1;
            var numIcon = num switch
            {
                1 => "1️⃣", 2 => "2️⃣", 3 => "3️⃣", 4 => "4️⃣", 5 => "5️⃣",
                6 => "6️⃣", 7 => "7️⃣", 8 => "8️⃣", 9 => "9️⃣", 10 => "🔟",
                _ => $"#{num}"
            };

            var encodedTitle = WebUtility.HtmlEncode(art.Title);
            var primaryUrl = art.SourceUrls.FirstOrDefault() ?? "https://infoskrot.pl";
            var domain = GetDomain(primaryUrl);
            var teaser = CleanTeaser(art.SummaryText, 140);
            var encodedTeaser = WebUtility.HtmlEncode(teaser);

            sb.AppendLine($"{numIcon} <b>{encodedTitle}</b>");
            sb.AppendLine($"   🔗 <a href=\"{primaryUrl}\">{domain}</a> • <i>{encodedTeaser}</i>");
            sb.AppendLine();
        }

        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"<i>Kliknij numer poniżej, aby wybrać (maks. {maxLimit})</i>");

        var text = sb.ToString();
        if (text.Length > 4000)
        {
            text = text[..3950] + "\n\n<i>[...skrócono listę...]</i>";
        }

        var keyboard = BuildThematicMarkup(articles);

        try
        {
            var msg = await ExecuteWithRetryAsync(() => client.SendMessage(
                chatId: new ChatId(chatId),
                text: text,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken), cancellationToken);

            return msg.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania zbiorczej partii {Region} na Telegram", region);
            return 0;
        }
    }

    public async Task UpdateThematicBatchMarkupAsync(long messageId, SourceRegion region, IReadOnlyList<NewsArticle> articles, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            return;
        }

        var keyboard = BuildThematicMarkup(articles);

        try
        {
            await client.EditMessageReplyMarkup(
                chatId: new ChatId(chatId),
                messageId: (int)messageId,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się zaktualizować klawiatury dla partii tematycznej {MessageId}", messageId);
        }
    }

    public async Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken cancellationToken = default)
    {
        var isMock = _telegramOptions.UseMock;
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(callbackQueryId))
        {
            return;
        }

        try
        {
            await client.AnswerCallbackQuery(callbackQueryId, text, showAlert: showAlert, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się odpowiedzieć na CallbackQuery {Id}", callbackQueryId);
        }
    }

    private static InlineKeyboardMarkup BuildThematicMarkup(IReadOnlyList<NewsArticle> articles)
    {
        var buttons = new List<InlineKeyboardButton>();
        for (var i = 0; i < articles.Count; i++)
        {
            var num = i + 1;
            var isSel = articles[i].IsSelectedForDigest;
            var text = isSel ? $"✅ {num}" : $"{num}";
            buttons.Add(InlineKeyboardButton.WithCallbackData(text, $"curate_toggle:{articles[i].Id}"));
        }

        // Podział przycisków na wiersze po 5
        var rows = buttons.Chunk(5).Select(chunk => chunk.ToArray()).ToArray();
        return new InlineKeyboardMarkup(rows);
    }

    private string BuildControlPanelText(int polandCount, int worldCount, int positiveCount)
    {
        var total = polandCount + worldCount + positiveCount;
        return "📊 <b>PANEL STEROWANIA WYDANIA INFOSKRÓT</b>\n" +
               "━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
               $"🇵🇱 <b>Polska:</b> {polandCount} / {_curationOptions.MaxPolandArticles} wybrano\n" +
               $"🌍 <b>Świat:</b> {worldCount} / {_curationOptions.MaxWorldArticles} wybrano\n" +
               $"☀️ <b>Pozytywne:</b> {positiveCount} / {_curationOptions.MaxPositiveArticles} wybrano\n\n" +
               $"Razem zakwalifikowano: <b>{total}</b> artykułów\n" +
               "━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
               "Wybierz numery artykułów w wiadomościach powyżej.\n" +
               "Gdy skończysz selekcję, kliknij przycisk poniżej, aby zatwierdzić i opublikować wydanie:";
    }

    private static InlineKeyboardMarkup BuildControlPanelMarkup()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🚀 ZATWIERDŹ I PUBLIKUJ WYDANIE", "digest_publish") }
        });
    }

    private static string GetDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host;
            return host.StartsWith("www.") ? host[4..] : host;
        }
        catch
        {
            return "źródło";
        }
    }

    private static string CleanTeaser(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
        while (singleLine.Contains("  "))
        {
            singleLine = singleLine.Replace("  ", " ");
        }

        if (singleLine.Length <= maxLength) return singleLine;
        return singleLine[..maxLength].TrimEnd() + "...";
    }

    public async Task<long> SendSimpleNotificationAsync(string message, CancellationToken cancellationToken = default)
    {
        var chatIdStr = _telegramOptions.AdminChatId;
        var isMock = _telegramOptions.UseMock;
        var client = ResolveClient();

        if (isMock || client == null || string.IsNullOrWhiteSpace(chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            _logger.LogInformation("TelegramNotificationService [MOCK]: {Message}", message);
            return Random.Shared.Next(1000, 99999);
        }

        try
        {
            var msg = await ExecuteWithRetryAsync(() => client.SendMessage(
                chatId: new ChatId(chatId),
                text: message,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken), cancellationToken);

            return msg.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania powiadomienia na Telegram");
            return 0;
        }
    }
}
