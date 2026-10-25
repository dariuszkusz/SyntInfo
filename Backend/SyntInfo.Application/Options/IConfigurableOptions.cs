namespace SyntInfo.Application.Options;

/// <summary>
/// Interfejs oznaczający klasy opcji konfiguracyjnych powiązane z sekcją w konfiguracji (appsettings.json).
/// Umożliwia automatyczną, generyczną rejestrację opcji na podstawie skanowania asynchronicznego/refleksyjnego assembly.
/// </summary>
public interface IConfigurableOptions
{
    /// <summary>
    /// Nazwa sekcji konfiguracyjnej w appsettings.json. Pusty ciąg znaków oznacza powiązanie z głównym poziomem konfiguracji.
    /// </summary>
    string SectionName { get; }
}
