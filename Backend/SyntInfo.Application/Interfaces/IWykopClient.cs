namespace SyntInfo.Application.Interfaces;

public interface IWykopClient
{
    /// <summary>
    /// Tworzy wpis na mikroblogu Wykop v3 (POST /entries).
    /// </summary>
    Task<string> CreateEntryAsync(string content, string? photoKey = null, CancellationToken cancellationToken = default);
}
