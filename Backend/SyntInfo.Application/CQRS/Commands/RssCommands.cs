using System;

namespace SyntInfo.Application.CQRS.Commands
{
    public record TriggerRssFetchCommand();
    
    public record SummarizeArticleCommand(
        string Title, 
        string Content, 
        string Url, 
        DateTime PublishedAt, 
        Guid SourceId,
        SyntInfo.Domain.Entities.SourceRegion Region);

    public record ClearAllArticlesCommand();
}
