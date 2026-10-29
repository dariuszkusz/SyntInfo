using SyntInfo.Application.CQRS.Commands;

namespace SyntInfo.Application.CQRS.Handlers;

/// <summary>
/// Handler CQRS wysyłający partię artykułów do selekcji na Telegramie podzieloną na 3 wiadomości tematyczne (Polska, Świat, Pozytywne)
/// oraz 1 panel sterowania wydaniem.
/// </summary>
public static class SendCurationBatchCommandHandler
{
    public static async Task<SendCurationBatchResult> Handle(
        SendCurationBatchCommand _,
        IUnitOfWork uow,
        ITelegramNotificationService telegramService,
        ITelegramCurationStateService telegramState,
        IOptions<CurationOptions> curationOptions,
        CancellationToken cancellationToken)
    {
        var opts = curationOptions.Value;
        telegramState.Clear();

        var polandArticles = await uow.NewsArticles.GetArticlesForCurationByRegionAsync(SourceRegion.Poland, opts.ProposalPolandCount, cancellationToken);
        var worldArticles = await uow.NewsArticles.GetArticlesForCurationByRegionAsync(SourceRegion.World, opts.ProposalWorldCount, cancellationToken);
        var positiveArticles = await uow.NewsArticles.GetArticlesForCurationByRegionAsync(SourceRegion.Positive, opts.ProposalPositiveCount, cancellationToken);

        var totalCount = polandArticles.Count + worldArticles.Count + positiveArticles.Count;

        if (totalCount == 0)
        {
            return new SendCurationBatchResult(
                Success: true,
                SentCount: 0,
                Message: "Brak nowych artykułów oczekujących na selekcję do wydania.");
        }

        // 1. Wiadomość zbiorcza: Polska
        if (polandArticles.Any())
        {
            var polandMsgId = await telegramService.SendThematicBatchAsync(SourceRegion.Poland, polandArticles, cancellationToken);
            if (polandMsgId > 0)
            {
                telegramState.SetCategoryMessageId(SourceRegion.Poland, polandMsgId);
            }
            await Task.Delay(300, cancellationToken);
        }

        // 2. Wiadomość zbiorcza: Świat
        if (worldArticles.Any())
        {
            var worldMsgId = await telegramService.SendThematicBatchAsync(SourceRegion.World, worldArticles, cancellationToken);
            if (worldMsgId > 0)
            {
                telegramState.SetCategoryMessageId(SourceRegion.World, worldMsgId);
            }
            await Task.Delay(300, cancellationToken);
        }

        // 3. Wiadomość zbiorcza: Pozytywne
        if (positiveArticles.Any())
        {
            var positiveMsgId = await telegramService.SendThematicBatchAsync(SourceRegion.Positive, positiveArticles, cancellationToken);
            if (positiveMsgId > 0)
            {
                telegramState.SetCategoryMessageId(SourceRegion.Positive, positiveMsgId);
            }
            await Task.Delay(300, cancellationToken);
        }

        // 4. Panel Kontrolny Wydania z podsumowaniem liczników
        var polandSelected = polandArticles.Count(a => a.IsSelectedForDigest);
        var worldSelected = worldArticles.Count(a => a.IsSelectedForDigest);
        var positiveSelected = positiveArticles.Count(a => a.IsSelectedForDigest);

        var panelMsgId = await telegramService.SendDigestControlPanelAsync(polandSelected, worldSelected, positiveSelected, cancellationToken);
        if (panelMsgId > 0)
        {
            telegramState.ControlPanelMessageId = panelMsgId;
        }

        return new SendCurationBatchResult(
            Success: true,
            SentCount: totalCount,
            Message: $"Wysłano {totalCount} artykułów w 3 kategoriach na Telegram (Polska: {polandArticles.Count}, Świat: {worldArticles.Count}, Pozytywne: {positiveArticles.Count}) wraz z panelem sterowania.");
    }
}
