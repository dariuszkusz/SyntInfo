namespace SyntInfo.Application.Interfaces
{
    public interface IOpenRouterClient
    {
        /// <summary>
        /// Krok 1: Analityk - Generuje ustrukturyzowaną listę faktów (JSON) na podstawie RSS i danych z wyszukiwarki.
        /// </summary>
        Task<string> GenerateFactsAsync(string rssContent, string searchContent, CancellationToken cancellationToken = default);

        /// <summary>
        /// Krok 2: Redaktor - Generuje minimalistyczne podsumowanie w języku polskim na podstawie faktów.
        /// </summary>
        Task<string> GenerateSummaryFromFactsAsync(string factsJson, CancellationToken cancellationToken = default);

        /// <summary>
        /// Wybiera najważniejsze artykuły z listy.
        /// </summary>
        Task<List<int>> SelectTopArticlesIndexesAsync(string articlesListJson, int expectedCount, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generuje wektory (embeddings) dla tekstu.
        /// </summary>
        Task<float[]> GenerateEmbeddingsAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pobiera informacje o użyciu klucza i limitach.
        /// </summary>
        Task<string> GetUsageAsync(CancellationToken cancellationToken = default);
    }
}
