using Microsoft.AspNetCore.Authentication;
using Microsoft.OpenApi.Models;
using SyntInfo.Api.Authentication;

namespace SyntInfo.Api.Extensions;

/// <summary>
/// Metody rozszerzające IServiceCollection dla konfiguracji API, Swaggera, autentykacji i CORS.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Konfiguruje generowanie specyfikacji OpenAPI i Swagger z obsługą klucza administracyjnego (X-Admin-Key / Bearer).
    /// </summary>
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Name = "X-Admin-Key",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Description = "Wprowadź klucz administracyjny w nagłówku X-Admin-Key lub Authorization: Bearer <key>"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "ApiKey"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    /// <summary>
    /// Konfiguruje uwierzytelnianie kluczem API oraz autoryzację opartą o rolę Admin.
    /// </summary>
    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = ApiKeyAuthenticationHandler.SchemeName;
            options.DefaultChallengeScheme = ApiKeyAuthenticationHandler.SchemeName;
        })
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationHandler.SchemeName, null);

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
        });

        return services;
    }

    /// <summary>
    /// Konfiguruje politykę CORS dla aplikacji frontendowej Angular na podstawie dozwolonych originów.
    /// Zezwala wyłącznie na zaufane originy skonfigurowane w FrontendUrl, eliminując ryzyko CORS misconfiguration.
    /// </summary>
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var frontendUrl = configuration["FrontendUrl"] ?? "http://localhost:4200";
        var allowedOrigins = frontendUrl
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToArray();

        if (allowedOrigins.Length == 0)
        {
            allowedOrigins = new[] { "http://localhost:4200" };
        }

        services.AddCors(options =>
        {
            options.AddPolicy("AllowAngular", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

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
                var botToken = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Telegram:BotToken"];
                return new Telegram.Bot.TelegramBotClient(botToken ?? string.Empty, httpClient);
            });

        return services;
    }
}
