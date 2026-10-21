using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Tavily;
using SyntInfo.Domain.Entities;

namespace SyntInfo.Infrastructure.Services
{
    public class TavilyNewsDiscoveryService : INewsDiscoveryService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<TavilyNewsDiscoveryService> _logger;

        public TavilyNewsDiscoveryService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<TavilyNewsDiscoveryService> logger)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Search:TavilyApiKey"] ?? string.Empty;
            _logger = logger;
        }

        public async Task<List<DiscoveredNewsItem>> DiscoverNewsAsync(SourceRegion region, CancellationToken cancellationToken = default)
        {
            var discovered = new List<DiscoveredNewsItem>();

            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.Contains("PLACEHOLDER") || _apiKey.Contains("YOUR-KEY"))
            {
                _logger.LogWarning("Tavily API Key nie jest skonfigurowany. Pomijanie discovery dla regionu {Region}.", region);
                return discovered;
            }

            try
            {
                string query = region switch
                {
                    SourceRegion.Poland => "najważniejsze wiadomości z Polski z ostatnich 24 godzin",
                    SourceRegion.Positive => "pozytywne dobre wiadomości ze świata nauka sukcesy inspirujące ostatnie 24 godziny",
                    _ => "top world news headlines last 24 hours"
                };

                var request = new
                {
                    api_key = _apiKey,
                    query = query,
                    search_depth = "advanced",
                    max_results = 8
                };

                var response = await _httpClient.PostAsJsonAsync("https://api.tavily.com/search", request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Błąd zapytania do Tavily API ({StatusCode}) dla regionu {Region}.", response.StatusCode, region);
                    return discovered;
                }

                var result = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(cancellationToken: cancellationToken);
                if (result?.Results == null) return discovered;

                foreach (var res in result.Results)
                {
                    if (!string.IsNullOrWhiteSpace(res.Title) && !string.IsNullOrWhiteSpace(res.Url))
                    {
                        discovered.Add(new DiscoveredNewsItem
                        {
                            Title = res.Title,
                            Content = res.Content ?? string.Empty,
                            Url = res.Url,
                            PublishedAt = DateTime.UtcNow,
                            Region = region
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas News Discovery dla regionu {Region}.", region);
            }

            return discovered;
        }
    }
}
