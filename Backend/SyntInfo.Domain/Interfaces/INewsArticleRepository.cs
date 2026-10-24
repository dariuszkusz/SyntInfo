using SyntInfo.Domain.Entities;

namespace SyntInfo.Domain.Interfaces;

public interface INewsArticleRepository : IRepository<NewsArticle>
{
    Task<bool> ExistsByUrlOrTitleAsync(string url, string title, CancellationToken cancellationToken = default);
}
