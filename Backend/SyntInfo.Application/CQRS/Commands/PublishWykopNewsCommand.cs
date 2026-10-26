namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda pobierająca wiadomości z bazy danych i publikująca je na mikroblogu Wykop.
/// </summary>
public record PublishWykopNewsCommand(int? Count = null);

public record PublishWykopNewsResult(
    bool Success,
    int PublishedArticlesCount,
    string? EntryId,
    string Message,
    string? Error = null);
