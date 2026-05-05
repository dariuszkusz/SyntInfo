namespace SyntInfo.Application.DTOs
{
    public class TopNewsResponse
    {
        public List<NewsArticleDto> Poland { get; set; } = new();
        public List<NewsArticleDto> World { get; set; } = new();
    }
}
