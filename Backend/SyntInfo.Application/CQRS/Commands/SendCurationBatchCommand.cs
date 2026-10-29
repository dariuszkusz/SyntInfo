namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda wysyłająca partię artykułów do selekcji na Telegramie wraz z panelem sterowania wydaniem.
/// </summary>
public record SendCurationBatchCommand();

public record SendCurationBatchResult(
    bool Success,
    int SentCount,
    string Message,
    string? Error = null);
