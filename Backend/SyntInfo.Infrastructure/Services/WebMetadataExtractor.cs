using System.Text.RegularExpressions;

namespace SyntInfo.Infrastructure.Services;

public class WebMetadataExtractor : IWebMetadataExtractor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebMetadataExtractor> _logger;

    public WebMetadataExtractor(HttpClient httpClient, ILogger<WebMetadataExtractor> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    }

    public async Task<string?> TryExtractOgImageAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            // Odczytaj nagłówek HTML (pierwsze 64 KB)
            char[] buffer = new char[65536];
            int read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0) return null;

            var htmlHead = new string(buffer, 0, read);
            var match = Regex.Match(htmlHead, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(htmlHead, @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']", RegexOptions.IgnoreCase);
            }
            if (!match.Success)
            {
                match = Regex.Match(htmlHead, @"<meta[^>]+name=[""']twitter:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            }

            if (match.Success)
            {
                var imgUrl = match.Groups[1].Value;
                if (imgUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return imgUrl;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Nie udało się pobrać og:image z {Url}", url);
        }

        return null;
    }
}
