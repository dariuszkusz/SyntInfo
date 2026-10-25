using Scrutor;
using SyntInfo.Infrastructure;

namespace SyntInfo.Api.Extensions;

/// <summary>
/// Metody rozszerzające IServiceCollection do automatycznej rejestracji usług infrastruktury i publisherów przez Scrutor.
/// </summary>
public static class InfrastructureExtensions
{
    /// <summary>
    /// Rejestruje usługi infrastruktury oraz publishery platform społecznościowych
    /// przy użyciu biblioteki Scrutor na podstawie skanowania assembly i konwencji interfejsów.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblyOf<InfrastructureAssemblyMarker>()
            .AddClasses(classes => classes.AssignableTo<ISocialPlatformPublisher>())
                .As<ISocialPlatformPublisher>()
                .WithScopedLifetime()
            .AddClasses(classes => classes.InNamespaces(
                "SyntInfo.Infrastructure.Services",
                "SyntInfo.Infrastructure.Services.Publishers",
                "SyntInfo.Infrastructure.Persistence"))
                .UsingRegistrationStrategy(RegistrationStrategy.Skip)
                .AsMatchingInterface()
                .WithScopedLifetime());

        return services;
    }
}
