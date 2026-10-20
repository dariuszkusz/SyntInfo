namespace SyntInfo.Application.Interfaces;

public interface IMailerLiteClient
{
    /// <summary>
    /// Tworzy i natychmiast planuje/wysyła kampanię newslettera w MailerLite.
    /// </summary>
    Task<string> SendNewsletterCampaignAsync(string subject, string htmlContent, string plainTextContent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje adres e-mail do bazy subskrybentów MailerLite.
    /// </summary>
    Task<bool> SubscribeAsync(string email, string? name = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wypisuje adres e-mail z bazy subskrybentów MailerLite (ustawia status unsubscribed).
    /// </summary>
    Task<bool> UnsubscribeAsync(string email, CancellationToken cancellationToken = default);
}
