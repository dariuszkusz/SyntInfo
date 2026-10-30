namespace SyntInfo.Infrastructure.Services;

/// <summary>
/// Bezpieczny helper do pobierania załączników graficznych z publicznych adresów HTTP/HTTPS.
/// Blokuje dostęp do lokalnego systemu plików oraz prywatnych adresów sieciowych (ochrona przed SSRF i Local File Disclosure).
/// </summary>
internal static class RemoteImageDownloader
{
    public static async Task<(byte[] Bytes, string FileName)?> DownloadImageAsync(
        IHttpClientFactory httpClientFactory,
        string imageUrl,
        string defaultFileName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.IsLoopback)
        {
            logger.LogWarning("Odrzucono nieprawidłowy lub lokalny adres URL obrazka: {Url}", imageUrl);
            return null;
        }

        var downloadClient = httpClientFactory.CreateClient();
        var imageBytes = await downloadClient.GetByteArrayAsync(uri, cancellationToken);

        var fileName = defaultFileName;
        var lastSegment = Path.GetFileName(uri.LocalPath);
        if (!string.IsNullOrWhiteSpace(lastSegment) && lastSegment.Contains('.'))
        {
            fileName = lastSegment;
        }

        return (imageBytes, fileName);
    }

    public static string GetMediaType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }
}

