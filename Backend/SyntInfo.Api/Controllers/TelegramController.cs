using System.Text.Json;
using SyntInfo.Application.CQRS.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Wolverine;
using Microsoft.AspNetCore.Authorization;

namespace SyntInfo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class TelegramController : ControllerBase
{
    private readonly IMessageBus _bus;
    private readonly ITelegramBotClient _botClient;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TelegramController> _logger;

    public TelegramController(
        IMessageBus bus,
        ITelegramBotClient botClient,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<TelegramController> logger)
    {
        _bus = bus;
        _botClient = botClient;
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook([FromBody] JsonElement rawUpdate, CancellationToken cancellationToken)
    {
        // Weryfikacja nagłówka Secret Token (wymuszana w Production/Staging, opcjonalna w Dev jeśli brak konfiguracji)
        var expectedSecret = _configuration["Telegram:SecretToken"];
        var isSecretConfigured = !string.IsNullOrWhiteSpace(expectedSecret) && !expectedSecret.Contains("PLACEHOLDER");

        if (_env.IsProduction() || _env.IsStaging() || isSecretConfigured)
        {
            if (!isSecretConfigured)
            {
                _logger.LogError("Krytyczny błąd bezpieczeństwa: Telegram:SecretToken nie został skonfigurowany w środowisku {Environment}.", _env.EnvironmentName);
                return StatusCode(StatusCodes.Status500InternalServerError, "Błąd konfiguracji zabezpieczeń serwera.");
            }

            if (!Request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var secretHeader) || secretHeader != expectedSecret)
            {
                _logger.LogWarning("Odrzucono webhook Telegram: brak lub błędny nagłówek X-Telegram-Bot-Api-Secret-Token");
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
            return Ok(); // Status 200 OK zapobiega pętli ponowień (retry) przez serwer Telegrama
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
            if (!_configuration.GetValue("Telegram:UseMock", false))
            {
                try
                {
                    await _botClient.AnswerCallbackQuery(callback.Id, "Przetwarzanie decyzji...", cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Nie udało się wysłać AnswerCallbackQuery do Telegrama");
                }
            }

            var parts = data.Split(':', 2);
            var action = parts[0].ToLowerInvariant();
            var chatId = callback.Message?.Chat.Id;
            var messageId = callback.Message?.Id;

            if (action == "digest_publish")
            {
                _logger.LogInformation("Wyzwalanie publikacji wydania InfoSkrót z poziomu Telegrama.");
                await _bus.PublishAsync(new PublishDigestIssueCommand(chatId, messageId));
            }
            else if (action == "curate_toggle" && parts.Length == 2 && Guid.TryParse(parts[1], out var articleId))
            {
                _logger.LogInformation("Przełączanie selekcji artykułu {ArticleId} do wydania.", articleId);
                await _bus.PublishAsync(new ToggleArticleDigestSelectionCommand(articleId, chatId, messageId));
            }
            else if (parts.Length == 2 && Guid.TryParse(parts[1], out var draftId))
            {
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
    /// Ręczne wyzwolenie wysyłki partii artykułów do selekcji i publikacji na platformy społecznościowe.
    /// Deleguje logikę do handlera CQRS (SendCurationBatchCommandHandler).
    /// </summary>
    [HttpPost("trigger-drafts")]
    public async Task<IActionResult> TriggerDrafts([FromQuery] int count = 5, CancellationToken cancellationToken = default)
    {
        var result = await _bus.InvokeAsync<SendCurationBatchResult>(
            new SendCurationBatchCommand(count),
            cancellationToken);

        return Ok(result);
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

        var rawSecret = _configuration["Telegram:SecretToken"];
        string? secretToken = null;

        if (!string.IsNullOrWhiteSpace(rawSecret) && !rawSecret.Contains("PLACEHOLDER"))
        {
            // Telegram dopuszcza wyłącznie znaki [A-Za-z0-9_-]
            secretToken = new string(rawSecret.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        }

        try
        {
            await _botClient.SetWebhook(
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
    /// Deleguje logikę do handlera CQRS (SendTestSocialDraftCommandHandler).
    /// </summary>
    [HttpPost("send-test-draft")]
    public async Task<IActionResult> SendTestDraft(
        [FromQuery] SocialPlatformType platform = SocialPlatformType.Wykop,
        CancellationToken cancellationToken = default)
    {
        var result = await _bus.InvokeAsync<SendTestDraftResult>(
            new SendTestSocialDraftCommand(platform),
            cancellationToken);

        return result.Success ? Ok(result) : StatusCode(500, result);
    }

    /// <summary>
    /// Wysyła partię najnowszych artykułów do selekcji przez administratora na Telegramie
    /// wraz z panelem sterowania do wygenerowania i publikacji zbiorczego wydania InfoSkrót.
    /// Deleguje logikę do handlera CQRS (SendCurationBatchCommandHandler).
    /// </summary>
    [HttpPost("send-curation-batch")]
    public async Task<IActionResult> SendCurationBatch(
        [FromQuery] int count = 5,
        CancellationToken cancellationToken = default)
    {
        var result = await _bus.InvokeAsync<SendCurationBatchResult>(
            new SendCurationBatchCommand(count),
            cancellationToken);

        return Ok(result);
    }
}

