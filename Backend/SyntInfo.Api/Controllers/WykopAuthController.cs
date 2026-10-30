using Microsoft.AspNetCore.Authorization;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/Wykop")]
[Authorize(Roles = "Admin")]
public class WykopAuthController : ControllerBase
{
    private readonly IMessageBus _bus;
    private readonly AppOptions _appOptions;

    public WykopAuthController(
        IMessageBus bus,
        IOptions<AppOptions> appOptions)
    {
        _bus = bus;
        _appOptions = appOptions.Value;
    }

    /// <summary>
    /// Pobiera link autoryzacyjny Wykop Connect do powiązania konta użytkownika.
    /// </summary>
    [HttpGet("connect-url")]
    public async Task<IActionResult> GetConnectUrl(CancellationToken cancellationToken)
    {
        var result = await _bus.InvokeAsync<GetWykopConnectUrlResult>(
            new GetWykopConnectUrlCommand(),
            cancellationToken);

        return result.Success ? Ok(result) : StatusCode(500, result);
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

        var result = await _bus.InvokeAsync<SaveWykopUserTokensResult>(
            new SaveWykopUserTokensCommand(token, rtoken),
            cancellationToken);

        return result.Success
            ? Redirect($"{frontendUrl}/settings/integrations?status=wykop_connected")
            : Redirect($"{frontendUrl}/settings/integrations?error=save_failed");
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

        var result = await _bus.InvokeAsync<SaveWykopUserTokensResult>(
            new SaveWykopUserTokensCommand(dto.Token, dto.RefreshToken),
            cancellationToken);

        return result.Success ? Ok(result) : StatusCode(500, result);
    }
}
