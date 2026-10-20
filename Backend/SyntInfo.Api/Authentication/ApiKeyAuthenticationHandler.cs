using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SyntInfo.Api.Authentication;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdminApiKey";
    public const string HeaderName = "X-Admin-Key";

    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredKey = _configuration["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Contains("PLACEHOLDER"))
        {
            Logger.LogError("Klucz Admin:ApiKey nie został poprawnie skonfigurowany w User Secrets lub zmiennych środowiskowych.");
            return Task.FromResult(AuthenticateResult.Fail("Admin API Key nie jest poprawnie skonfigurowany na serwerze."));
        }

        string? providedKey = null;

        // 1. Sprawdź nagłówek X-Admin-Key
        if (Request.Headers.TryGetValue(HeaderName, out var headerValue))
        {
            providedKey = headerValue.ToString();
        }
        // 2. Sprawdź nagłówek Authorization: Bearer <Key> lub ApiKey <Key>
        else if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authHeaderStr = authHeader.ToString();
            if (authHeaderStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                providedKey = authHeaderStr["Bearer ".Length..].Trim();
            }
            else if (authHeaderStr.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            {
                providedKey = authHeaderStr["ApiKey ".Length..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        byte[] providedBytes = System.Text.Encoding.UTF8.GetBytes(providedKey);
        byte[] configuredBytes = System.Text.Encoding.UTF8.GetBytes(configuredKey);

        if (providedBytes.Length != configuredBytes.Length || 
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail("Nieprawidłowy klucz administracyjny."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin"),
            new Claim(ClaimTypes.Name, "Administrator"),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
