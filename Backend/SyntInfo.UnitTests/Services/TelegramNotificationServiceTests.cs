using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Infrastructure.Services;
using Telegram.Bot;
using Xunit;

namespace SyntInfo.UnitTests.Services;

public class TelegramNotificationServiceTests
{
    private readonly ILogger<TelegramNotificationService> _logger = Substitute.For<ILogger<TelegramNotificationService>>();
    private readonly ITelegramBotClient _botClient = Substitute.For<ITelegramBotClient>();
    private readonly IOptions<TelegramOptions> _telegramOptions = Options.Create(new TelegramOptions
    {
        AdminChatId = "123456789",
        UseMock = true
    });
    private readonly IOptions<CurationOptions> _curationOptions = Options.Create(new CurationOptions
    {
        MaxPolandArticles = 5,
        MaxWorldArticles = 5,
        MaxPositiveArticles = 5,
        ProposalPolandCount = 10,
        ProposalWorldCount = 10,
        ProposalPositiveCount = 10
    });

    [Fact]
    public async Task SendThematicBatchAsync_With10ArticlesHavingLongUrlsAndTeasers_DoesNotThrowAndReturnsValidId()
    {
        // Arrange
        var service = new TelegramNotificationService(_telegramOptions, _logger, _curationOptions, _botClient);

        var articles = new List<NewsArticle>();
        for (int i = 1; i <= 10; i++)
        {
            articles.Add(new NewsArticle
            {
                Id = Guid.NewGuid(),
                Title = $"Bardzo długi tytuł artykułu numer {i} z wieloma słowami o polityce międzynarodowej i gospodarce światowej",
                SummaryText = $"Oto bardzo szczegółowe i długie podsumowanie artykułu {i}, które zawiera wiele zdań i informacji faktograficznych na temat bieżących wydarzeń.",
                SourceUrls = new List<string>
                {
                    $"https://news.google.com/rss/articles/CBMiYmh0dHBzOi8vd3d3LnJldXRlcnMuY29tL3dvcmxkL21pZGRsZS1lYXN0L2lzcmFlbC1zdHJpa2VzLWdhemEtY2Vhc2VmaXJlLXRhbGtzLTIwMjYtMTAtMzAv0gEA?oc=5&utm_source=syntinfo_{i}&region=world"
                },
                Region = SourceRegion.World,
                IsActive = true
            });
        }

        // Act
        var result = await service.SendThematicBatchAsync(SourceRegion.World, articles, CancellationToken.None);

        // Assert
        Assert.True(result > 0);
    }
}
