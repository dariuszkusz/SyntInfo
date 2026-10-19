using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/Wykop")]
public class WykopAuthController : ControllerBase
{
    private readonly IWykopClient _wykopClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WykopAuthController> _logger;

    public WykopAuthController(
        IWykopClient wykopClient,
        IConfiguration configuration,
        ILogger<WykopAuthController> logger)
    {
        _wykopClient = wykopClient;
        _configuration = configuration;
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
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? token,
        [FromQuery] string? rtoken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { Success = false, Error = "Brak parametru token w odpowiedzi z Wykop Connect." });
        }

        try
        {
            await _wykopClient.SaveUserTokensAsync(token, rtoken, cancellationToken);
            _logger.LogInformation("Pomyślnie powiązano konto użytkownika Wykop (token JWT: {TokenPrefix}...).", token[..Math.Min(15, token.Length)]);

            var html = @"<!DOCTYPE html>
<html lang=""pl"">
<head>
    <meta charset=""UTF-8"">
    <title>SyntInfo - Wykop Powiązany</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0f172a; color: #f8fafc; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }
        .card { background: #1e293b; padding: 2.5rem; border-radius: 1rem; border: 1px solid #334155; text-align: center; max-width: 500px; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }
        h1 { color: #38bdf8; margin-top: 0; font-size: 1.6rem; }
        p { color: #94a3b8; line-height: 1.6; }
        .badge { display: inline-block; background: #065f46; color: #34d399; padding: 0.4rem 1rem; border-radius: 9999px; font-weight: 600; font-size: 0.9rem; margin-bottom: 1rem; }
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""badge"">POŁĄCZONO SUKCESEM</div>
        <h1>Konto Wykop powiązane!</h1>
        <p>Token sesyjny użytkownika został pomyślnie zapisany w systemie <strong>SyntInfo</strong>.</p>
        <p>Możesz teraz zamknąć tę kartę i zatwierdzić publikację posta w bocie na Telegramie.</p>
    </div>
</body>
</html>";
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas zapisu tokenu Wykop Connect");
            return StatusCode(500, new { Success = false, Error = ex.Message });
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
