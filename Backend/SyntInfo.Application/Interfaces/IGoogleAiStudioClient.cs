namespace SyntInfo.Application.Interfaces
{
    public interface IGoogleAiStudioClient
    {
        /// <summary>
        /// Krok 2: Redaktor - Generuje minimalistyczne podsumowanie w języku polskim na podstawie faktów.
        /// Wykorzystuje Google AI Studio, a w przypadku błędu (np. brak tokenów) przełącza się na OpenRouter.
        /// </summary>
        Task<string> GenerateSummaryFromFactsAsync(string factsJson, CancellationToken cancellationToken = default);
    }
}
