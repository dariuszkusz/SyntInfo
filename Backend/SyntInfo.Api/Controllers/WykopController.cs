using Microsoft.AspNetCore.Authorization;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class WykopController : ControllerBase
{
    private readonly IMessageBus _bus;

    public WykopController(IMessageBus bus) => _bus = bus;

    /// <summary>
    /// Pobiera określoną liczbę wiadomości z bazy danych i publikuje je na mikroblogu Wykop.
    /// Jeśli parametr count nie zostanie podany, używana jest wartość domyślna z konfiguracji CurationOptions.
    /// </summary>
    [HttpPost("publish-news")]
    public async Task<IActionResult> PublishWykopNews(
        [FromQuery] int? count = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _bus.InvokeAsync<PublishWykopNewsResult>(
            new PublishWykopNewsCommand(count),
            cancellationToken);

        if (!result.Success && result.Error == "Brak artykułów.")
        {
            return NotFound(result);
        }

        return result.Success ? Ok(result) : StatusCode(500, result);
    }
}
