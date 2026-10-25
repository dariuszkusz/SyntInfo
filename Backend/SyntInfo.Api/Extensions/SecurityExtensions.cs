using Microsoft.AspNetCore.Authentication;
using SyntInfo.Api.Authentication;

namespace SyntInfo.Api.Extensions;

/// <summary>
/// Metody rozszerzające IServiceCollection dla autentykacji, autoryzacji i polityki CORS.
/// </summary>
public static class SecurityExtensions
{
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
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, AppOptions appOptions)
    {
        var frontendUrl = appOptions.FrontendUrl ?? "http://localhost:4200";
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
}
