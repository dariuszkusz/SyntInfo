using Microsoft.OpenApi.Models;

namespace SyntInfo.Api.Extensions;

/// <summary>
/// Metody rozszerzające IServiceCollection do konfiguracji dokumentacji OpenAPI i Swagger.
/// </summary>
public static class SwaggerExtensions
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
}
