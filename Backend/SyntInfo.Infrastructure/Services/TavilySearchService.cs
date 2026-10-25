using System.Net.Http.Json;
using SyntInfo.Application.Models.Tavily;

namespace SyntInfo.Infrastructure.Services;

public class TavilySearchService : ISearchService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<TavilySearchService> _logger;

    public TavilySearchService(HttpClient httpClient, IOptions<TavilyOptions> tavilyOptions, ILogger<TavilySearchService> logger)
    {
        _httpClient = httpClient;
        _apiKey = tavilyOptions.Value.TavilyApiKey;
        _logger = logger;
    }

    public async Task<string> SearchDetailedInfoAsync(string query, CancellationToken cancellationToken = default)
    {
        var res = await SearchDetailedWithImageAsync(query, cancellationToken);
        return res.Content;
    }

    public async Task<SearchResultInfo> SearchDetailedWithImageAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.Contains("YOUR-KEY"))
        {
            _logger.LogWarning("Tavily API Key is missing or default. Returning fallback message.");
            return new SearchResultInfo { Content = "[WARNING] Tavily API Key not configured. Detailed search results are not available." };
        }

        try
        {
            var request = new
            {
                api_key = _apiKey,
                query = query,
                search_depth = "advanced",
                include_answer = true,
                include_images = true,
                max_results = 5
            };

            var response = await _httpClient.PostAsJsonAsync("https://api.tavily.com/search", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Tavily API error: {StatusCode}. Details: {Error}", response.StatusCode, error);
                return new SearchResultInfo { Content = $"[ERROR] Search failed with status {response.StatusCode}" };
            }

            var result = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(cancellationToken: cancellationToken);

            if (result == null || (string.IsNullOrEmpty(result.Answer) && (result.Results == null || !result.Results.Any())))
            {
                return new SearchResultInfo { Content = "No detailed search results found." };
            }

            string? imageUrl = result.Images?.FirstOrDefault(img => !string.IsNullOrWhiteSpace(img) && (img.StartsWith("http://") || img.StartsWith("https://")));

            string content;
            if (!string.IsNullOrEmpty(result.Answer))
            {
                content = result.Answer;
            }
            else
            {
                content = string.Join("\n\n", result.Results.Select(r => $"Title: {r.Title}\nContent: {r.Content}"));
            }

            return new SearchResultInfo
            {
                Content = content,
                ImageUrl = imageUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Tavily search for query: {Query}", query);
            return new SearchResultInfo { Content = $"[EXCEPTION] Search failed: {ex.Message}" };
        }
    }
}
