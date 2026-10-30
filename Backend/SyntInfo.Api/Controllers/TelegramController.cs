using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using SyntInfo.Application.CQRS.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Wolverine;

namespace SyntInfo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class TelegramController : ControllerBase
{
    private readonly IMessageBus _bus;
    private readonly TelegramOptions _telegramOptions;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TelegramController> _logger;

    public TelegramController(
        IMessageBus bus,
        IOptions<TelegramOptions> telegramOptions,
        IWebHostEnvironment env,
        ILogger<TelegramController> logger)
    {
        _bus = bus;
        _telegramOptions = telegramOptions.Value;
        _env = env;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook([FromBody] JsonElement rawUpdate, CancellationToken cancellationToken)
    {
        var expectedSecret = _telegramOptions.SecretToken;
        var isSecretConfigured = !string.IsNullOrWhiteSpace(expectedSecret) && !expectedSecret.Contains("PLACEHOLDER");

        if (_env.IsProduction() || _env.IsStaging() || isSecretConfigured)
        {
            if (!isSecretConfigured)
            {
                _logger.LogError("Krytyczny błąd bezpieczeństwa: Telegram:SecretToken nie został skonfigurowany w środowisku {Environment}.", _env.EnvironmentName);
                return StatusCode(StatusCodes.Status500InternalServerError, "Błąd konfiguracji zabezpieczeń serwera.");
            }

            if (!Request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var secretHeader) ||
                !IsSecretValid(secretHeader.ToString(), expectedSecret!))
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

        if (update?.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            var callback = update.CallbackQuery;
            await _bus.InvokeAsync(
                new ProcessTelegramCallbackCommand(
                    CallbackQueryId: callback.Id,
                    Data: callback.Data,
                    Username: callback.From?.Username,
                    ChatId: callback.Message?.Chat.Id,
                    MessageId: callback.Message?.Id),
                cancellationToken);
        }

        return Ok();
    }

    private static bool IsSecretValid(string provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    public record SetupWebhookDto(string? WebhookUrl);

    /// <summary>
    /// Rejestracja webhooka w oficjalnym API Telegrama dla bota.
    /// Deleguje logikę do handlera CQRS (SetupTelegramWebhookCommand).
    /// </summary>
    [HttpPost("setup-webhook")]
    public async Task<IActionResult> SetupWebhook(
        [FromBody] SetupWebhookDto? body = null,
        [FromQuery] string? webhookUrl = null,
        CancellationToken cancellationToken = default)
    {
        var targetUrl = body?.WebhookUrl ?? webhookUrl;
        var result = await _bus.InvokeAsync<SetupTelegramWebhookResult>(
            new SetupTelegramWebhookCommand(targetUrl),
            cancellationToken);

        if (result.IsValidationError)
        {
            return BadRequest(new { result.Error });
        }

        return result.Success
            ? Ok(new { result.Success, result.Message })
            : StatusCode(500, new { result.Success, result.Error });
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
    [HttpPost("trigger-drafts")]
    [HttpPost("send-curation-batch")]
    public async Task<IActionResult> SendCurationBatch(CancellationToken cancellationToken = default)
    {
        var result = await _bus.InvokeAsync<SendCurationBatchResult>(
            new SendCurationBatchCommand(),
            cancellationToken);

        return Ok(result);
    }
}
