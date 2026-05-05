using Microsoft.AspNetCore.Mvc;
using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wolverine;

namespace SyntInfo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NewsController : ControllerBase
    {
        private readonly IMessageBus _bus;

        public NewsController(IMessageBus bus)
        {
            _bus = bus;
        }

        [HttpGet]
        public async Task<ActionResult<List<NewsArticleDto>>> GetArticles([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] SyntInfo.Domain.Entities.SourceRegion? region = null)
        {
            var query = new GetNewsArticlesQuery(page, pageSize, region);
            var result = await _bus.InvokeAsync<List<NewsArticleDto>>(query, HttpContext.RequestAborted);
            return Ok(result);
        }

        [HttpGet("top")]
        public async Task<ActionResult<TopNewsResponse>> GetTopNews()
        {
            var result = await _bus.InvokeAsync<TopNewsResponse>(new GetTopNewsQuery(), HttpContext.RequestAborted);
            return Ok(result);
        }

        [HttpPost("sync")]
        public async Task<IActionResult> TriggerSync()
        {
            // Wyzwalamy procesy tła ręcznie
            await _bus.PublishAsync(new TriggerRssFetchCommand());
            await _bus.PublishAsync(new CleanupOldArticlesCommand(DaysToKeep: 7));

            return Accepted(new { Message = "Ręczna synchronizacja uruchomiona." });
        }

        [HttpPost("clear")]
        public async Task<IActionResult> ClearArticles()
        {
            await _bus.PublishAsync(new ClearAllArticlesCommand());
            return Ok(new { Message = "Baza danych została wyczyszczona." });
        }
    }
}
