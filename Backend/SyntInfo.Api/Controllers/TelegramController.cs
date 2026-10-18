using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Wolverine;

using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SyntInfo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelegramController : ControllerBase
{
    private readonly IMessageBus _bus;
    private readonly IUnitOfWork _uow;
    private readonly ITelegramNotificationService _telegramService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramController> _logger;

    public TelegramController(
        IMessageBus bus,
        IUnitOfWork uow,
        ITelegramNotificationService telegramService,
        IConfiguration configuration,
        ILogger<TelegramController> logger)
    {
        _bus = bus;
        _uow = uow;
        _telegramService = telegramService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook([FromBody] JsonElement rawUpdate)
    {
        // Opcjonalna walidacja nagłówka Secret Token
        var expectedSecret = _configuration["Telegram:SecretToken"];
        if (!string.IsNullOrWhiteSpace(expectedSecret) && !expectedSecret.Contains("PLACEHOLDER"))
        {
            if (!Request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var secretHeader) || secretHeader != expectedSecret)
            {
                _logger.LogWarning("Odrzucono webhook Telegram: brak lub błędny X-Telegram-Bot-Api-Secret-Token");
                return Unauthorized();
            }
        }

        Update? update;
        try
        {
            var json = rawUpdate.GetRawText();
            update = JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd deserializacji aktualizacji Telegram z webhooka");
            return BadRequest();
        }

        if (update == null)
        {
            return Ok();
        }

        // Obsługa interaktywnych przycisków (CallbackQuery)
        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            var callback = update.CallbackQuery;
            var data = callback.Data ?? string.Empty;
            _logger.LogInformation("Odebrano CallbackQuery z Telegrama: Data='{Data}', From='{User}'", data, callback.From.Username);

            // Odpowiedz botowi na Telegramie, aby zdjąć stan ładowania przycisku
            var token = _configuration["Telegram:BotToken"];
            if (!string.IsNullOrWhiteSpace(token) && !token.Contains("PLACEHOLDER") && !_configuration.GetValue("Telegram:UseMock", false))
            {
                try
                {
                    var botClient = new TelegramBotClient(token);
                    await botClient.AnswerCallbackQuery(callback.Id, "Przetwarzanie decyzji...");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Nie udało się wysłać AnswerCallbackQuery do Telegrama");
                }
            }

            var parts = data.Split(':', 2);
            if (parts.Length == 2 && Guid.TryParse(parts[1], out var draftId))
            {
                var action = parts[0].ToLowerInvariant();
                var chatId = callback.Message?.Chat.Id;
                var messageId = callback.Message?.Id;

                if (action == "approve")
                {
                    _logger.LogInformation("Wyzwalanie publikacji posta dla szkicu {DraftId}", draftId);
                    await _bus.PublishAsync(new ApproveAndPublishSocialPostCommand(draftId, chatId, messageId));
                }
                else if (action == "reject")
                {
                    _logger.LogInformation("Wyzwalanie odrzucenia szkicu {DraftId}", draftId);
                    await _bus.PublishAsync(new RejectSocialPostDraftCommand(draftId, chatId, messageId));
                }
            }
        }

        return Ok();
    }

    /// <summary>
    /// Ręczne wyzwolenie generowania szkiców postów dla administratora (do celów testowych/operacyjnych).
    /// </summary>
    [HttpPost("trigger-drafts")]
    public async Task<IActionResult> TriggerDrafts([FromQuery] Guid? articleId = null, [FromQuery] int? limit = null)
    {
        await _bus.PublishAsync(new CreateSocialPostDraftsCommand(articleId, limit));
        return Accepted(new { Message = "Rozpoczęto generowanie szkiców postów na platformy społecznościowe." });
    }

    public record SetupWebhookDto(string? WebhookUrl);

    /// <summary>
    /// Rejestracja webhooka w oficjalnym API Telegrama dla bota.
    /// Przyjmuje adres webhookUrl w ciele żądania (JSON {"webhookUrl": "..."} lub surowy tekst) bądź w query stringu.
    /// </summary>
    [HttpPost("setup-webhook")]
    public async Task<IActionResult> SetupWebhook([FromBody] SetupWebhookDto? body = null, [FromQuery] string? webhookUrl = null)
    {
        var targetUrl = body?.WebhookUrl ?? webhookUrl;
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return BadRequest(new { Error = "Brak podanego adresu webhookUrl w ciele żądania lub query." });
        }

        var token = _configuration["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(token) || token.Contains("PLACEHOLDER"))
        {
            return BadRequest(new { Error = "Brak skonfigurowanego Telegram:BotToken." });
        }

        var botClient = new TelegramBotClient(token);
        var rawSecret = _configuration["Telegram:SecretToken"];
        string? secretToken = null;

        if (!string.IsNullOrWhiteSpace(rawSecret) && !rawSecret.Contains("PLACEHOLDER"))
        {
            // Telegram dopuszcza wyłącznie znaki [A-Za-z0-9_-]
            secretToken = new string(rawSecret.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        }

        try
        {
            await botClient.SetWebhook(
                url: targetUrl,
                secretToken: !string.IsNullOrWhiteSpace(secretToken) ? secretToken : null);

            _logger.LogInformation("Webhook zarejestrowany w Telegramie: {Url}", targetUrl);
            return Ok(new { Success = true, Message = $"Webhook zarejestrowany na adres {targetUrl}." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas rejestracji webhooka w Telegram API");
            return StatusCode(500, new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// Wysyła testowy szkic posta bezpośrednio do administratora na Telegram z przyciskami [✅ Opublikuj] i [❌ Odrzuć].
    /// </summary>
    [HttpPost("send-test-draft")]
    public async Task<IActionResult> SendTestDraft([FromQuery] SocialPlatformType platform = SocialPlatformType.Hejto)
    {
        // 1. Sprawdź lub utwórz przykładowy artykuł
        var article = await _uow.Repository<NewsArticle>().Query()
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (article == null)
        {
            article = new NewsArticle
            {
                Title = "Przełom w fuzji jądrowej - SyntInfo AI",
                OriginalTitle = "Breakthrough in Nuclear Fusion",
                SummaryText = "Europejscy inżynierowie osiągnęli stabilną plazmę fuzyjną przez ponad 12 minut. Jest to rekordowy wynik przybliżający komercyjne reaktory nowej generacji.",
                PublishedAt = DateTime.UtcNow,
                SourceUrls = new List<string> { "https://syntinfo.local/news/fusion" },
                IsActive = true
            };
            await _uow.Repository<NewsArticle>().AddAsync(article, HttpContext.RequestAborted);
            await _uow.SaveChangesAsync(HttpContext.RequestAborted);
        }

        // 2. Utwórz szkic posta
        var draft = new SocialPostDraft
        {
            NewsArticleId = article.Id,
            PlatformType = platform,
            Content = $"⚡ {article.Title}\n\n{article.SummaryText}\n\n🔗 Czytaj więcej: {article.SourceUrls.FirstOrDefault()}\n#technologia #nauka #syntinfo",
            Status = SocialPostStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        await _uow.Repository<SocialPostDraft>().AddAsync(draft, HttpContext.RequestAborted);
        await _uow.SaveChangesAsync(HttpContext.RequestAborted);

        // 3. Wyślij do administratora przez TelegramNotificationService
        try
        {
            var msgId = await _telegramService.SendDraftToAdminAsync(draft, article, HttpContext.RequestAborted);
            if (msgId > 0)
            {
                draft.TelegramMessageId = msgId;
                _uow.Repository<SocialPostDraft>().Update(draft);
                await _uow.SaveChangesAsync(HttpContext.RequestAborted);
            }

            return Ok(new
            {
                Success = true,
                DraftId = draft.Id,
                Platform = platform.ToString(),
                TelegramMessageId = msgId,
                Message = $"Wysłano testowy szkic posta ({platform}) na Telegram!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania testowego szkicu do Telegrama");
            return StatusCode(500, new { Success = false, Error = ex.Message });
        }
    }
}
