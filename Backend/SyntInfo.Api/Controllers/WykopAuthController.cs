using Microsoft.AspNetCore.Authorization;

namespace SyntInfo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/Wykop")]
[Authorize(Roles = "Admin")]
public class WykopAuthController : ControllerBase
{
    private readonly IWykopClient _wykopClient;
    private readonly AppOptions _appOptions;
    private readonly ILogger<WykopAuthController> _logger;

    public WykopAuthController(
        IWykopClient wykopClient,
        IOptions<AppOptions> appOptions,
        ILogger<WykopAuthController> logger)
    {
        _wykopClient = wykopClient;
        _appOptions = appOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Pobiera link autoryzacyjny Wykop Connect do powiązania konta użytkownika.
    /// </summary>
    [HttpGet("connect-url")]
    public async Task<IActionResult> GetConnectUrl(CancellationToken cancellationToken)
    {
        try
        {
            var url = await _wykopClient.GetConnectUrlAsync(cancellationToken);
            return Ok(new
            {
                Success = true,
                ConnectUrl = url,
                Instruction = "Otwórz ten adres w przeglądarce, zaloguj się na Wykopie i kliknij 'Połącz konto'. Po zaakceptowaniu nastąpi przekierowanie z tokenem autoryzacyjnym."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas generowania linku Wykop Connect");
            return StatusCode(500, new { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// Adres zwrotny (Callback) wywoływany przez Wykop po akceptacji uprawnień przez użytkownika.
    /// Przekierowuje użytkownika do widoku integracji w aplikacji frontendowej (Angular).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? token,
        [FromQuery] string? rtoken,
        CancellationToken cancellationToken)
    {
        var frontendUrl = _appOptions.FrontendUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(token))
        {
            return Redirect($"{frontendUrl}/settings/integrations?error=missing_token");
        }

        try
        {
            await _wykopClient.SaveUserTokensAsync(token, rtoken, cancellationToken);
            _logger.LogInformation("Pomyślnie powiązano konto użytkownika Wykop.");

            return Redirect($"{frontendUrl}/settings/integrations?status=wykop_connected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas zapisu tokenu Wykop Connect");
            return Redirect($"{frontendUrl}/settings/integrations?error=save_failed");
        }
    }

    public record SetUserTokenDto(string Token, string? RefreshToken);

    /// <summary>
    /// Ręczne ustawienie tokenu użytkownika (jeśli użytkownik skopiował parametry token i rtoken z adresu URL).
    /// </summary>
    [HttpPost("set-user-token")]
    public async Task<IActionResult> SetUserToken([FromBody] SetUserTokenDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
        {
            return BadRequest(new { Success = false, Error = "Pole Token nie może być puste." });
        }

        await _wykopClient.SaveUserTokensAsync(dto.Token, dto.RefreshToken, cancellationToken);
        return Ok(new { Success = true, Message = "Token użytkownika Wykop został pomyślnie zapisany w systemie." });
    }
}
