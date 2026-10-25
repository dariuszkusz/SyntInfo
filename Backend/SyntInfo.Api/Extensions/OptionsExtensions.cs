using System.Reflection;

namespace SyntInfo.Api.Extensions;

/// <summary>
/// Metody rozszerzające IServiceCollection do automatycznej rejestracji silnie typowanych opcji IOptions.
/// </summary>
public static class OptionsExtensions
{
    /// <summary>
    /// Skanuje assembly typu znacznikowego <typeparamref name="TMarker"/> i automatycznie rejestruje w kontenerze DI
    /// wszystkie klasy opcji implementujące interfejs <see cref="IConfigurableOptions"/>, powiązując je z sekcjami konfiguracji.
    /// </summary>
    public static IServiceCollection AddConfiguredOptions<TMarker>(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddConfiguredOptionsFromAssembly(configuration, typeof(TMarker).Assembly);

    /// <summary>
    /// Skanuje wskazane assembly i automatycznie rejestruje w kontenerze DI wszystkie klasy opcji
    /// implementujące interfejs <see cref="IConfigurableOptions"/>, powiązując je z odpowiednimi sekcjami konfiguracji.
    /// </summary>
    public static IServiceCollection AddConfiguredOptionsFromAssembly(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly assembly)
    {
        var configureMethod = typeof(OptionsConfigurationServiceCollectionExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(OptionsConfigurationServiceCollectionExtensions.Configure)
                         && m.GetGenericArguments().Length == 1
                         && m.GetParameters().Length == 2
                         && m.GetParameters()[0].ParameterType == typeof(IServiceCollection)
                         && m.GetParameters()[1].ParameterType == typeof(IConfiguration));

        var optionsTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IConfigurableOptions).IsAssignableFrom(t));

        foreach (var type in optionsTypes)
        {
            var instance = (IConfigurableOptions)Activator.CreateInstance(type)!;
            var sectionName = instance.SectionName;

            IConfiguration configSection = string.IsNullOrWhiteSpace(sectionName)
                ? configuration
                : configuration.GetSection(sectionName);

            var genericMethod = configureMethod.MakeGenericMethod(type);
            genericMethod.Invoke(null, new object[] { services, configSection });
        }

        return services;
    }
}
