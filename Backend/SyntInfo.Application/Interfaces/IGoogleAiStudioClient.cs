namespace SyntInfo.Application.Interfaces
{
    public interface IGoogleAiStudioClient
    {
        /// <summary>
        /// Krok 1: Analityk - Wybiera najważniejsze artykuły z listy.
        /// </summary>
        Task<List<int>> SelectTopArticlesIndexesAsync(string articlesListJson, int expectedCount, CancellationToken cancellationToken = default);

        /// <summary>
        /// Krok 1: Analityk - Generuje ustrukturyzowaną listę faktów (JSON) na podstawie treści i danych z wyszukiwarki.
        /// </summary>
        Task<string> GenerateFactsAsync(string content, string searchContent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Krok 2: Redaktor - Generuje minimalistyczne podsumowanie w języku polskim na podstawie faktów.
        /// Wykorzystuje Google AI Studio, a w przypadku błędu (np. brak tokenów) przełącza się na OpenRouter.
        /// </summary>
        Task<string> GenerateSummaryFromFactsAsync(string factsJson, CancellationToken cancellationToken = default);
    }
}
