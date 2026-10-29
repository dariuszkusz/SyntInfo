using Microsoft.AspNetCore.Authorization;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class HejtoController : ControllerBase
{
    private readonly IMessageBus _bus;

    public HejtoController(IMessageBus bus) => _bus = bus;

    /// <summary>
    /// Pobiera artykuły z bazy danych i publikuje każdy artykuł jako osobny wpis wraz z załącznikiem graficznym na portalu Hejto.
    /// Jeśli parametr count nie zostanie podany, używane są artykuły zaznaczone do wydania lub domyślna liczba z konfiguracji CurationOptions.
    /// </summary>
    [HttpPost("publish-articles-separately")]
    public async Task<IActionResult> PublishArticlesSeparately(
        [FromQuery] int? count = null,
        [FromQuery] string? community = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _bus.InvokeAsync<PublishHejtoArticlesSeparatelyResult>(
            new PublishHejtoArticlesSeparatelyCommand(
                ArticleIds: null,
                Count: count,
                CommunitySlug: community),
            cancellationToken);

        if (!result.Success && result.Error == "Brak artykułów.")
        {
            return NotFound(result);
        }

        return result.Success ? Ok(result) : StatusCode(500, result);
    }
}
