namespace SyntInfo.Application.CQRS.Commands
{
    public record TriggerRssFetchCommand();

    public record SummarizeArticleCommand(
        string Title,
        string Content,
        string Url,
        DateTime PublishedAt,
        Guid SourceId,
        Domain.Entities.SourceRegion Region,
        List<string>? AdditionalUrls = null);

    public record ClearAllArticlesCommand();
}
