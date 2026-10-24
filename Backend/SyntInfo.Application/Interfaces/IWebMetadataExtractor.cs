namespace SyntInfo.Application.Interfaces;

public interface IWebMetadataExtractor
{
    Task<string?> TryExtractOgImageAsync(string url, CancellationToken cancellationToken = default);
}
