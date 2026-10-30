namespace SyntInfo.Application.CQRS.Commands;

/// <summary>
/// Komenda pobierająca link autoryzacyjny Wykop Connect do połączenia konta użytkownika.
/// </summary>
public record GetWykopConnectUrlCommand();

public record GetWykopConnectUrlResult(
    bool Success,
    string? ConnectUrl = null,
    string? Instruction = null,
    string? Error = null);

/// <summary>
/// Komenda zapisująca tokeny użytkownika (UserToken / RefreshToken) z procedury Wykop Connect.
/// </summary>
public record SaveWykopUserTokensCommand(string UserToken, string? RefreshToken = null);

public record SaveWykopUserTokensResult(
    bool Success,
    string? Message = null,
    string? Error = null);

