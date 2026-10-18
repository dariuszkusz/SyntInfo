using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SyntInfo.Application.CQRS.Commands;
using Wolverine;

namespace SyntInfo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NewsletterController : ControllerBase
{
    private readonly IMessageBus _bus;

    public NewsletterController(IMessageBus bus)
    {
        _bus = bus;
    }

    /// <summary>
    /// Zapisuje adres e-mail do newslettera.
    /// </summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new NewsletterOperationResult(false, "Podaj poprawny adres e-mail."));
        }

        var result = await _bus.InvokeAsync<NewsletterOperationResult>(
            new SubscribeNewsletterCommand(request.Email, request.Name),
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Wypisuje adres e-mail z newslettera.
    /// </summary>
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new NewsletterOperationResult(false, "Podaj poprawny adres e-mail."));
        }

        var result = await _bus.InvokeAsync<NewsletterOperationResult>(
            new UnsubscribeNewsletterCommand(request.Email),
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    public record SubscribeRequest(
        [Required(ErrorMessage = "Adres e-mail jest wymagany.")]
        [EmailAddress(ErrorMessage = "Podaj poprawny format adresu e-mail.")]
        string Email,
        string? Name = null);

    public record UnsubscribeRequest(
        [Required(ErrorMessage = "Adres e-mail jest wymagany.")]
        [EmailAddress(ErrorMessage = "Podaj poprawny format adresu e-mail.")]
        string Email);
}
