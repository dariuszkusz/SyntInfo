namespace SyntInfo.Application.Models.Tavily;

public class TavilySearchResponse
{
    public string? Answer { get; set; }
    public List<TavilyResult> Results { get; set; } = new();
    public List<string>? Images { get; set; }
}
