using System.Threading;
using System.Threading.Tasks;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.Infrastructure.Services
{
    public class MockSearchService : ISearchService
    {
        public Task<string> SearchDetailedInfoAsync(string query, CancellationToken cancellationToken = default)
        {
            // Zwracanie sztucznych wyników z wyszukiwarki do testów.
            return Task.FromResult($"[MOCK SEARCH RESULT] Specjalne pogłębione tło i analiza: Według zewnętrznych wyszukiwań na temat: '{query}', sytuacja rozwija się dynamicznie. Służby przekazały nowe informacje, jest to jedno z ważniejszych zdarzeń w regionie. Pamiętaj, aby uwzględnić ten kontekst w ostatecznym podsumowaniu merytorycznym.");
        }
    }
}
