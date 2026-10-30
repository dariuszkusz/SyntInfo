using Microsoft.EntityFrameworkCore;
using SyntInfo.Application.CQRS.Commands;
using Wolverine.Attributes;

namespace SyntInfo.Application.CQRS.Handlers;

/// <summary>
/// Asynchroniczny handler CQRS publikujący pojedynczy artykuł na portalu Hejto.
/// Wywoływany jako opóźniony komunikat (Wolverine Scheduled Message), co eliminuje blokowanie wątku
/// oraz chroni przed limitem anty-flood API Hejto (kod 429 TooManyRequests).
/// </summary>
public static class PublishSingleHejtoArticleCommandHandler
{
    [MessageTimeout(120)]
    public static async Task Handle(
        PublishSingleHejtoArticleCommand command,
        IUnitOfWork uow,
        IHejtoClient hejtoClient,
        ILogger<PublishSingleHejtoArticleCommand> logger,
        CancellationToken cancellationToken)
    {
        var article = await uow.NewsArticles.Query()
            .Include(a => a.Category)
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, cancellationToken);

        if (article == null)
        {
            logger.LogWarning("Nie znaleziono artykułu {ArticleId} do zaplanowanej publikacji na Hejto.", command.ArticleId);
            return;
        }

        string? imageUuid = null;
        if (!string.IsNullOrWhiteSpace(article.ImageUrl))
        {
            try
            {
                logger.LogInformation("Przesyłanie załącznika graficznego do Hejto dla artykułu ({Index}/{Total}) '{Title}': {Url}",
                    command.Index, command.TotalCount, article.Title, article.ImageUrl);
                imageUuid = await hejtoClient.UploadImageAsync(article.ImageUrl, cancellationToken);
            }
            catch (Exception uploadEx)
            {
                logger.LogWarning(uploadEx, "Nie udało się przesłać obrazka do Hejto dla artykułu '{Title}'. Kontynuacja bez załącznika.", article.Title);
            }
        }

        var postContent = PublishHejtoArticlesSeparatelyCommandHandler.FormatSingleArticleForHejto(article);
        try
        {
            var postId = await hejtoClient.CreatePostAsync(
                postContent,
                command.CommunitySlug,
                imageUuid,
                cancellationToken);

            logger.LogInformation(
                "Pomyślnie opublikowano zaplanowany artykuł ({Index}/{Total}) '{Title}' na Hejto (ID: {PostId}, Załącznik: {HasAttachment}).",
                command.Index,
                command.TotalCount,
                article.Title,
                postId,
                !string.IsNullOrWhiteSpace(imageUuid));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Błąd podczas publikowania zaplanowanego artykułu ({Index}/{Total}) '{Title}' ({ArticleId}) na Hejto: {Error}",
                command.Index, command.TotalCount, article.Title, article.Id, ex.Message);
            throw;
        }
    }
}
