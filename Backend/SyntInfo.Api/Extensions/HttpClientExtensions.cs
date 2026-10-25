namespace SyntInfo.Api.Extensions;

/// <summary>
/// Metody rozszerzające IServiceCollection do konfiguracji klientów HTTP (IHttpClientFactory).
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// Skanuje assembly typu <typeparamref name="TMarker"/> i automatycznie konfiguruje nazwane klienty HttpClient
    /// dla wszystkich klas opcji implementujących interfejs <see cref="IHttpClientOptions"/>.
    /// </summary>
    public static IServiceCollection AddConfiguredHttpClients<TMarker>(this IServiceCollection services)
    {
        var clientOptionsTypes = typeof(TMarker).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IHttpClientOptions).IsAssignableFrom(t));

        foreach (var type in clientOptionsTypes)
        {
            var instance = (IHttpClientOptions)Activator.CreateInstance(type)!;
            services.AddHttpClient(instance.HttpClientName, client =>
            {
                client.Timeout = instance.Timeout;
            });
        }

        return services;
    }

    /// <summary>
    /// Rejestruje klienta ITelegramBotClient przy użyciu fabryki IHttpClientFactory,
    /// zapobiegając wyciekom gniazd (socket exhaustion) i ręcznemu tworzeniu instancji.
    /// </summary>
    public static IServiceCollection AddTelegramBotClient(this IServiceCollection services)
    {
        services.AddHttpClient("telegram_bot_client")
            .AddTypedClient<Telegram.Bot.ITelegramBotClient>((httpClient, sp) =>
            {
                var botToken = sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken;
                return new Telegram.Bot.TelegramBotClient(botToken ?? string.Empty, httpClient);
            });

        return services;
    }
}
