using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.Application.CQRS.Handlers;

public static class SubscribeNewsletterCommandHandler
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public static async Task<NewsletterOperationResult> Handle(
        SubscribeNewsletterCommand command,
        IMailerLiteClient mailerLiteClient,
        ILogger<SubscribeNewsletterCommand> logger,
        CancellationToken cancellationToken)
    {
        var email = command.Email?.Trim();

        if (string.IsNullOrWhiteSpace(email) || !EmailValidator.IsValid(email))
        {
            logger.LogWarning("Niepoprawny format adresu e-mail: '{Email}'", command.Email);
            return new NewsletterOperationResult(false, "Podaj poprawny adres e-mail.");
        }

        try
        {
            var success = await mailerLiteClient.SubscribeAsync(email, command.Name, cancellationToken);
            if (success)
            {
                logger.LogInformation("Pomyślnie zapisano {Email} do newslettera.", email);
                return new NewsletterOperationResult(true, "Dziękujemy! Pomyślnie zapisano do newslettera.");
            }

            logger.LogError("Niepowodzenie zapisu do newslettera dla {Email}", email);
            return new NewsletterOperationResult(false, "Wystąpił błąd podczas zapisu do newslettera. Spróbuj ponownie później.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Wyjątek podczas zapisu do newslettera dla {Email}", email);
            return new NewsletterOperationResult(false, "Wystąpił błąd serwera podczas przetwarzania zapisu.");
        }
    }
}

public static class UnsubscribeNewsletterCommandHandler
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public static async Task<NewsletterOperationResult> Handle(
        UnsubscribeNewsletterCommand command,
        IMailerLiteClient mailerLiteClient,
        ILogger<UnsubscribeNewsletterCommand> logger,
        CancellationToken cancellationToken)
    {
        var email = command.Email?.Trim();

        if (string.IsNullOrWhiteSpace(email) || !EmailValidator.IsValid(email))
        {
            logger.LogWarning("Niepoprawny format adresu e-mail przy wypisie: '{Email}'", command.Email);
            return new NewsletterOperationResult(false, "Podaj poprawny adres e-mail.");
        }

        try
        {
            var success = await mailerLiteClient.UnsubscribeAsync(email, cancellationToken);
            if (success)
            {
                logger.LogInformation("Pomyślnie wypisano {Email} z newslettera.", email);
                return new NewsletterOperationResult(true, "Twój adres e-mail został pomyślnie wypisany z newslettera.");
            }

            logger.LogError("Niepowodzenie wypisu z newslettera dla {Email}", email);
            return new NewsletterOperationResult(false, "Wystąpił błąd podczas wypisywania z newslettera. Spróbuj ponownie później.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Wyjątek podczas wypisu z newslettera dla {Email}", email);
            return new NewsletterOperationResult(false, "Wystąpił błąd serwera podczas przetwarzania wypisu.");
        }
    }
}
