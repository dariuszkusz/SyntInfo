namespace SyntInfo.Application.Options;

/// <summary>
/// Interfejs oznaczający opcje konfiguracji powiązane z nazwanym klientem HTTP (IHttpClientFactory).
/// Umożliwia automatyczną rejestrację i konfigurację klientów HttpClient na podstawie opcji.
/// </summary>
public interface IHttpClientOptions : IConfigurableOptions
{
    /// <summary>
    /// Nazwa klienta HTTP używana w fabryce IHttpClientFactory.CreateClient(name).
    /// </summary>
    string HttpClientName { get; }

    /// <summary>
    /// Limit czasu żądania HTTP. Domyślnie 30 sekund.
    /// </summary>
    TimeSpan Timeout => TimeSpan.FromSeconds(30);
}
