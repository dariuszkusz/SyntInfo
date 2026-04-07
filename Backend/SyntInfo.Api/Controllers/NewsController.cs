using Microsoft.AspNetCore.Mvc;
using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Application.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyntInfo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NewsController : ControllerBase
    {
        private readonly ICqrsBus _bus;

        public NewsController(ICqrsBus bus)
        {
            _bus = bus;
        }

        [HttpGet]
        public async Task<ActionResult<List<NewsArticleDto>>> GetArticles([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var query = new GetNewsArticlesQuery(page, pageSize);
            var result = await _bus.SendQueryAsync(query, HttpContext.RequestAborted);
            return Ok(result);
        }
    }
}
