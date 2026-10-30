namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda wyzwalająca automatyczną publikację wydania zbiorczego (tryb Fallback o 09:00),
/// jeśli redaktor nie zatwierdził jeszcze dzisiejszego wydania.
/// </summary>
public record TriggerDigestFallbackCommand();

