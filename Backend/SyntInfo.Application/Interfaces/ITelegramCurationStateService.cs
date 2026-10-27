namespace SyntInfo.Application.Interfaces;

/// <summary>
/// Usługa przechowująca stan bieżącej sesji kuratorskiej na Telegramie (ID wiadomości poszczególnych regionów i panelu kontrolnego).
/// </summary>
public interface ITelegramCurationStateService
{
    long? ControlPanelMessageId { get; set; }
    long? GetCategoryMessageId(SourceRegion region);
    void SetCategoryMessageId(SourceRegion region, long messageId);
    void Clear();
}
