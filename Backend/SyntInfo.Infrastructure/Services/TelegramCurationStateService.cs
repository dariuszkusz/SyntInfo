using System.Collections.Concurrent;

namespace SyntInfo.Infrastructure.Services;

/// <summary>
/// Wątkowo-bezpieczna implementacja singletonu przechowującego identyfikatory wiadomości Telegram dla aktywnej partii kuratorskiej.
/// </summary>
public class TelegramCurationStateService : ITelegramCurationStateService
{
    private readonly ConcurrentDictionary<SourceRegion, long> _categoryMessages = new();
    private long? _controlPanelMessageId;
    private readonly object _lock = new();

    public long? ControlPanelMessageId
    {
        get
        {
            lock (_lock)
            {
                return _controlPanelMessageId;
            }
        }
        set
        {
            lock (_lock)
            {
                _controlPanelMessageId = value;
            }
        }
    }

    public long? GetCategoryMessageId(SourceRegion region)
    {
        return _categoryMessages.TryGetValue(region, out var id) ? id : null;
    }

    public void SetCategoryMessageId(SourceRegion region, long messageId)
    {
        _categoryMessages[region] = messageId;
    }

    public void Clear()
    {
        _categoryMessages.Clear();
        ControlPanelMessageId = null;
    }
}
