namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda pobierająca wiadomości z bazy danych i publikująca każdy artykuł jako osobny wpis wraz z załącznikiem graficznym na portalu Hejto.
/// </summary>
public record PublishHejtoArticlesSeparatelyCommand(
    IReadOnlyList<Guid>? ArticleIds = null,
    int? Count = null,
    string? CommunitySlug = null);

public record PublishHejtoArticlesSeparatelyResult(
    bool Success,
    int TotalArticlesCount,
    int PublishedCount,
    IReadOnlyList<string> PostIds,
    string Message,
    string? Error = null);
