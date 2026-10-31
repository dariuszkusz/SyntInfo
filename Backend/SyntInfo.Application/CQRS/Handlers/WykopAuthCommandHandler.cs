using SyntInfo.Application.CQRS.Commands;

namespace SyntInfo.Application.CQRS.Handlers;

public static class WykopAuthCommandHandler
{
    public static async Task<GetWykopConnectUrlResult> Handle(
        GetWykopConnectUrlCommand _,
        IWykopClient wykopClient,
        ILogger<GetWykopConnectUrlCommand> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = await wykopClient.GetConnectUrlAsync(cancellationToken);
            return new GetWykopConnectUrlResult(
                Success: true,
                ConnectUrl: url,
                Instruction: "Otwórz ten adres w przeglądarce, zaloguj się na Wykopie i kliknij 'Połącz konto'. Po zaakceptowaniu nastąpi przekierowanie z tokenem autoryzacyjnym.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas generowania linku Wykop Connect");
            return new GetWykopConnectUrlResult(Success: false, Error: ex.Message);
        }
    }

    public static async Task<SaveWykopUserTokensResult> Handle(
        SaveWykopUserTokensCommand command,
        IWykopClient wykopClient,
        ILogger<SaveWykopUserTokensCommand> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.UserToken))
        {
            return new SaveWykopUserTokensResult(Success: false, Error: "Pole Token nie może być puste.");
        }

        try
        {
            await wykopClient.SaveUserTokensAsync(command.UserToken, command.RefreshToken, cancellationToken);
            logger.LogInformation("Pomyślnie powiązano konto użytkownika Wykop.");
            return new SaveWykopUserTokensResult(
                Success: true,
                Message: "Token użytkownika Wykop został pomyślnie zapisany w systemie.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas zapisu tokenu Wykop Connect");
            return new SaveWykopUserTokensResult(Success: false, Error: ex.Message);
        }
    }
}

