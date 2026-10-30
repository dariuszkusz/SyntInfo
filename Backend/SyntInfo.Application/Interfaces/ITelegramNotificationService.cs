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

    /// <summary>
    /// Wysyła kartę artykułu do preselekcji przez administratora z przyciskiem wyboru do wydania zbiorczego InfoSkrót.
    /// </summary>
    Task<long> SendArticleForCurationAsync(NewsArticle article, bool isSelected, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktualizuje przycisk i status artykułu w panelu selekcji (Do wydania / Pomiń).
    /// </summary>
    Task UpdateCurationMessageAsync(long messageId, NewsArticle article, bool isSelected, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wysyła panel kontrolny z liczbą wybranych artykułów i przyciskiem do wygenerowania i publikacji wydania.
    /// </summary>
    Task<long> SendDigestControlPanelAsync(int selectedCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wysyła panel kontrolny wydania z podziałem na wybrane artykuły z poszczególnych regionów (Polska, Świat, Pozytywne).
    /// </summary>
    Task<long> SendDigestControlPanelAsync(int polandCount, int worldCount, int positiveCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktualizuje panel kontrolny wydania z nowymi licznikami po zmianie selekcji artykułów.
    /// </summary>
    Task UpdateDigestControlPanelAsync(long messageId, int polandCount, int worldCount, int positiveCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wysyła zbiorczą wiadomość z listą artykułów dla danego regionu wraz z klawiaturą numeryczną.
    /// </summary>
    Task<long> SendThematicBatchAsync(SourceRegion region, IReadOnlyList<NewsArticle> articles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktualizuje klawiaturę numeryczną wiadomości zbiorczej dla danego regionu po zmianie selekcji.
    /// </summary>
    Task UpdateThematicBatchMarkupAsync(long messageId, SourceRegion region, IReadOnlyList<NewsArticle> articles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wysyła prostą wiadomość tekstową (raport lub powiadomienie) do administratora.
    /// </summary>
    Task<long> SendSimpleNotificationAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Odpowiada na zapytanie zwrotne (CallbackQuery) z Telegrama (np. powiadomienie lub alert przy przekroczeniu limitu).
    /// </summary>
    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejestruje adres URL webhooka w API Telegrama dla bota.
    /// </summary>
    Task SetWebhookAsync(string webhookUrl, string? secretToken = null, CancellationToken cancellationToken = default);
}
