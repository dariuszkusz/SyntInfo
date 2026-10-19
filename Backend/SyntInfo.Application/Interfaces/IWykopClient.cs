namespace SyntInfo.Application.Interfaces;

public interface IWykopClient
{
    /// <summary>
    /// Pobiera link autoryzacyjny Wykop Connect (GET /connect) do połączenia konta użytkownika z aplikacją.
    /// </summary>
    Task<string> GetConnectUrlAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje tokeny użytkownika (UserToken / RefreshToken) z procedury Wykop Connect.
    /// </summary>
    Task SaveUserTokensAsync(string userToken, string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tworzy wpis na mikroblogu Wykop v3 (POST /entries).
    /// </summary>
    Task<string> CreateEntryAsync(string content, string? photoKey = null, CancellationToken cancellationToken = default);
}

