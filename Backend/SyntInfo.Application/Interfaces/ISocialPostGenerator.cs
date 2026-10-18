using SyntInfo.Domain.Entities;

namespace SyntInfo.Application.Interfaces;

public interface ISocialPostGenerator
{
    /// <summary>
    /// Generuje spersonalizowaną treść posta dla wskazanej platformy na podstawie artykułu i jego faktów.
    /// </summary>
    Task<string> GenerateContentForPlatformAsync(SocialPlatformType platform, NewsArticle article, CancellationToken cancellationToken = default);
}
