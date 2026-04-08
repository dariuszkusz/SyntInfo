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
        public async Task<ActionResult<List<NewsArticleDto>>> GetArticles([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var query = new GetNewsArticlesQuery(page, pageSize);
            var result = await _bus.InvokeAsync<List<NewsArticleDto>>(query, HttpContext.RequestAborted);
            return Ok(result);
        }
    }
}
