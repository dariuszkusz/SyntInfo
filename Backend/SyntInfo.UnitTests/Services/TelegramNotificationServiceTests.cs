using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Infrastructure.Services;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Xunit;

namespace SyntInfo.UnitTests.Services;

public class TelegramNotificationServiceTests
{
    private readonly ILogger<TelegramNotificationService> _logger = Substitute.For<ILogger<TelegramNotificationService>>();
    private readonly ITelegramBotClient _botClient = Substitute.For<ITelegramBotClient>();
    private readonly IOptions<TelegramOptions> _telegramOptions = Options.Create(new TelegramOptions
    {
        AdminChatId = "123456789",
        BotToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11",
        UseMock = false
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
    public async Task SendThematicBatchAsync_With10ArticlesHavingLongUrlsAndTeasers_BuildsMessageWithinTelegramLimitAndSendsViaClient()
    {
        // Arrange
        SendMessageRequest? capturedRequest = null;
        _botClient.SendRequest(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRequest = callInfo.Arg<SendMessageRequest>();
                return new Message { Id = 777 };
            });

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
                IsActive = true,
                IsSelectedForDigest = i % 2 == 0
            });
        }

        // Act
        var result = await service.SendThematicBatchAsync(SourceRegion.World, articles, CancellationToken.None);

        // Assert
        result.Should().Be(777);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Text.Length.Should().BeLessThanOrEqualTo(4096);
        capturedRequest.Text.Should().Contain("WYDANIE DNIA — ŚWIAT");
        capturedRequest.ReplyMarkup.Should().NotBeNull();
        await _botClient.Received(1).SendRequest(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendDraftToAdminAsync_WhenUseMockFalse_FormatsHtmlAndSendsMessage()
    {
        // Arrange
        SendMessageRequest? capturedRequest = null;
        _botClient.SendRequest(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRequest = callInfo.Arg<SendMessageRequest>();
                return new Message { Id = 555 };
            });

        var service = new TelegramNotificationService(_telegramOptions, _logger, _curationOptions, _botClient);
        var article = new NewsArticle { Id = Guid.NewGuid(), Title = "Test <script> tytuł" };
        var draft = new SocialPostDraft
        {
            Id = Guid.NewGuid(),
            PlatformType = SocialPlatformType.Wykop,
            Content = "Treść wpisu & tagi #wiadomosci"
        };

        // Act
        var messageId = await service.SendDraftToAdminAsync(draft, article, CancellationToken.None);

        // Assert
        messageId.Should().Be(555);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Text.Should().Contain("&lt;script&gt;");
        capturedRequest.Text.Should().Contain("Treść wpisu &amp; tagi");
    }

    [Fact]
    public async Task SetWebhookAsync_InvokesBotClientSetWebhookRequest()
    {
        // Arrange
        SetWebhookRequest? capturedRequest = null;
        _botClient.SendRequest(Arg.Any<SetWebhookRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedRequest = callInfo.Arg<SetWebhookRequest>();
                return true;
            });

        var service = new TelegramNotificationService(_telegramOptions, _logger, _curationOptions, _botClient);

        // Act
        await service.SetWebhookAsync("https://example.com/api/Telegram/webhook", "my-secret", CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Url.Should().Be("https://example.com/api/Telegram/webhook");
        capturedRequest.SecretToken.Should().Be("my-secret");
    }
}
