using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;
using SyntInfo.Infrastructure.Models.Tavily;

namespace SyntInfo.Infrastructure.Services
{
    public class TavilySearchService : ISearchService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<TavilySearchService> _logger;

        public TavilySearchService(HttpClient httpClient, IConfiguration configuration, ILogger<TavilySearchService> logger)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Search:TavilyApiKey"] ?? string.Empty;
            _logger = logger;
        }

        public async Task<string> SearchDetailedInfoAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.Contains("YOUR-KEY"))
            {
                _logger.LogWarning("Tavily API Key is missing or default. Returning fallback message.");
                return "[WARNING] Tavily API Key not configured. Detailed search results are not available.";
            }

            try
            {
                var request = new
                {
                    api_key = _apiKey,
                    query = query,
                    search_depth = "advanced",
                    include_answer = true,
                    max_results = 5
                };

                var response = await _httpClient.PostAsJsonAsync("https://api.tavily.com/search", request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Tavily API error: {StatusCode}. Details: {Error}", response.StatusCode, error);
                    return $"[ERROR] Search failed with status {response.StatusCode}";
                }

                var result = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(cancellationToken: cancellationToken);

                if (result == null || (string.IsNullOrEmpty(result.Answer) && (result.Results == null || !result.Results.Any())))
                {
                    return "No detailed search results found.";
                }

                // If Tavily provided a direct AI answer, use it as it's already summarized
                if (!string.IsNullOrEmpty(result.Answer))
                {
                    return result.Answer;
                }

                // Otherwise, combine results
                var combinedResults = string.Join("\n\n", result.Results.Select(r => $"Title: {r.Title}\nContent: {r.Content}"));
                return combinedResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during Tavily search for query: {Query}", query);
                return $"[EXCEPTION] Search failed: {ex.Message}";
            }
        }
    }
}
