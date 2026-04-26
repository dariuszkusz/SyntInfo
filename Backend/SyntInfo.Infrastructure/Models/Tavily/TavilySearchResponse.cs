using System.Collections.Generic;

namespace SyntInfo.Infrastructure.Models.Tavily
{
    public class TavilySearchResponse
    {
        public string? Answer { get; set; }
        public List<TavilyResult> Results { get; set; } = new();
    }
}
