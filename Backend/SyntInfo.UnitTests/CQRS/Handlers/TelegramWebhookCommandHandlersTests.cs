using Microsoft.Extensions.Options;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Options;
using Wolverine;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class TelegramWebhookCommandHandlersTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ITelegramNotificationService _telegramService = Substitute.For<ITelegramNotificationService>();
    private readonly ILogger<ProcessTelegramCallbackCommand> _callbackLogger = Substitute.For<ILogger<ProcessTelegramCallbackCommand>>();
    private readonly ILogger<SetupTelegramWebhookCommand> _setupLogger = Substitute.For<ILogger<SetupTelegramWebhookCommand>>();

    [Fact]
    public async Task ProcessCallback_WhenDigestPublish_AnswersCallbackAndPublishesDigestCommand()
    {
        // Arrange
        var command = new ProcessTelegramCallbackCommand(
            CallbackQueryId: "cb-1",
            Data: "digest_publish",
            Username: "admin",
            ChatId: 12345,
            MessageId: 678);

        // Act
        await TelegramWebhookCommandHandlers.Handle(
            command, _bus, _telegramService, _callbackLogger, CancellationToken.None);

        // Assert
        await _telegramService.Received(1).AnswerCallbackQueryAsync(
            "cb-1", Arg.Any<string>(), false, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<PublishDigestIssueCommand>(c => c.TelegramChatId == 12345 && c.TelegramMessageId == 678));
    }

    [Fact]
    public async Task ProcessCallback_WhenCurateToggle_PublishesToggleCommandWithoutDuplicateCallbackAnswer()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var command = new ProcessTelegramCallbackCommand(
            CallbackQueryId: "cb-2",
            Data: $"curate_toggle:{articleId}",
            Username: "admin",
            ChatId: 12345,
            MessageId: 678);

        // Act
        await TelegramWebhookCommandHandlers.Handle(
            command, _bus, _telegramService, _callbackLogger, CancellationToken.None);

        // Assert
        await _telegramService.DidNotReceive().AnswerCallbackQueryAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<ToggleArticleDigestSelectionCommand>(c => c.ArticleId == articleId && c.CallbackQueryId == "cb-2"));
    }

    [Fact]
    public async Task SetupWebhook_SanitizesSecretTokenAndRegistersWebhook()
    {
        // Arrange
        var options = Options.Create(new TelegramOptions
        {
            BotToken = "12345:ValidToken",
            SecretToken = "my_secret-123!@#$%"
        });
        var command = new SetupTelegramWebhookCommand("https://infoskrot.pl/api/Telegram/webhook");

        // Act
        var result = await TelegramWebhookCommandHandlers.Handle(
            command, _telegramService, options, _setupLogger, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        await _telegramService.Received(1).SetWebhookAsync(
            "https://infoskrot.pl/api/Telegram/webhook",
            "my_secret-123",
            Arg.Any<CancellationToken>());
    }
}
