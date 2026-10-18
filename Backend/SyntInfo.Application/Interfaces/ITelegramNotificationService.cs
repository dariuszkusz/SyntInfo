using SyntInfo.Domain.Entities;

namespace SyntInfo.Application.Interfaces;

public interface ITelegramNotificationService
{
    /// <summary>
    /// Wysyła powiadomienie o nowym szkicu posta do administratora na Telegramie wraz z przyciskami [✅ Opublikuj] / [❌ Odrzuć].
    /// Zwraca wygenerowany Telegram Message ID, aby umożliwić późniejszą aktualizację wiadomości.
    /// </summary>
    Task<long> SendDraftToAdminAsync(SocialPostDraft draft, NewsArticle article, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktualizuje treść lub klawiaturę wiadomości w Telegramie po podjęciu decyzji przez administratora (np. usunięcie przycisków, dodanie statusu).
    /// </summary>
    Task UpdateDraftMessageAsync(long messageId, SocialPostDraft draft, string decisionSummary, CancellationToken cancellationToken = default);
}
