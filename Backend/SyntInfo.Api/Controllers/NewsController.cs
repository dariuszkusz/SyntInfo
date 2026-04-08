using Microsoft.AspNetCore.Mvc;
using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Application.Interfaces;
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
            var polandQuery = new GetNewsArticlesQuery(1, 10, SyntInfo.Domain.Entities.SourceRegion.Poland);
            var worldQuery = new GetNewsArticlesQuery(1, 10, SyntInfo.Domain.Entities.SourceRegion.World);

            var polandNews = await _bus.InvokeAsync<List<NewsArticleDto>>(polandQuery, HttpContext.RequestAborted);
            var worldNews = await _bus.InvokeAsync<List<NewsArticleDto>>(worldQuery, HttpContext.RequestAborted);

            return Ok(new TopNewsResponse
            {
                Poland = polandNews,
                World = worldNews
            });
        }
    }

    public class TopNewsResponse
    {
        public List<NewsArticleDto> Poland { get; set; } = new();
        public List<NewsArticleDto> World { get; set; } = new();
    }
}
