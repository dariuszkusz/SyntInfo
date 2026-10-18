namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda zapisu adresu e-mail do bazy subskrybentów newslettera.
/// </summary>
public record SubscribeNewsletterCommand(string Email, string? Name = null);

/// <summary>
/// Komenda wypisu adresu e-mail z bazy subskrybentów newslettera.
/// </summary>
public record UnsubscribeNewsletterCommand(string Email);

/// <summary>
/// Rezultat operacji newslettera zwracany do kontrolera.
/// </summary>
public record NewsletterOperationResult(bool Success, string Message);
