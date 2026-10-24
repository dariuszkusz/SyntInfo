namespace SyntInfo.Application.Interfaces;

public interface ISocialPostGenerator
{
    /// <summary>
    /// Generuje spersonalizowaną treść posta dla wskazanej platformy na podstawie pojedynczego artykułu.
    /// </summary>
    Task<string> GenerateContentForPlatformAsync(SocialPlatformType platform, NewsArticle article, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generuje zbiorcze wydanie (prasówkę / digest) dla wskazanej platformy z listy zaakceptowanych artykułów.
    /// </summary>
    Task<string> GenerateDigestContentForPlatformAsync(SocialPlatformType platform, IReadOnlyList<NewsArticle> articles, CancellationToken cancellationToken = default);
}

