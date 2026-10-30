namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda publikująca pojedynczy artykuł na portalu Hejto (wraz z uploadem załącznika graficznego).
/// Przeznaczona do harmonogramowania z opóźnieniem w Wolverine (Scheduled Message), aby ominąć limit anty-flood API Hejto bez blokowania wątku.
/// </summary>
public record PublishSingleHejtoArticleCommand(
    Guid ArticleId,
    string? CommunitySlug = null,
    int Index = 1,
    int TotalCount = 1);

