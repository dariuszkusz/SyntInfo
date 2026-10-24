using CodeHollow.FeedReader;

namespace SyntInfo.Infrastructure.Services;

public class RssFeedClient : IRssFeedClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RssFeedClient> _logger;

    public RssFeedClient(HttpClient httpClient, ILogger<RssFeedClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/xml, text/xml, application/rss+xml, */*");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
        _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        _httpClient.DefaultRequestHeaders.Add("Pragma", "no-cache");
    }

    public async Task<Feed?> FetchFeedAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var currentUrl = url;
            HttpResponseMessage response = null!;
            int redirectCount = 0;
            const int maxRedirects = 5;

            while (redirectCount <= maxRedirects)
            {
                response = await _httpClient.GetAsync(currentUrl, cancellationToken);
                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                {
                    var location = response.Headers.Location;
                    if (location != null)
                    {
                        if (!location.IsAbsoluteUri)
                        {
                            location = new Uri(new Uri(currentUrl), location);
                        }
                        currentUrl = location.ToString();
                        redirectCount++;
                        _logger.LogInformation("Podążanie za przekierowaniem RSS ({Count}/{Max}) do: {RedirectUrl}", redirectCount, maxRedirects, currentUrl);
                        continue;
                    }
                }
                break;
            }

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    _logger.LogWarning("Źródło RSS nie zostało znalezione (404): {SourceUrl}", url);
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    _logger.LogWarning("Dostęp do RSS zabroniony (403). Próba obejścia nagłówkami nie powiodła się: {SourceUrl}", url);
                else if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    _logger.LogWarning("RSS zwrócił przekierowanie {StatusCode} ({CodeInt}), którego HttpClient nie podążył: {SourceUrl}", response.StatusCode, (int)response.StatusCode, url);
                else
                    _logger.LogWarning("Błąd podczas pobierania RSS {StatusCode}: {SourceUrl}", response.StatusCode, url);

                return null;
            }

            var rssContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return FeedReader.ReadFromString(rssContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wystąpił błąd podczas pobierania feedu z {SourceUrl}", url);
            return null;
        }
    }
}
