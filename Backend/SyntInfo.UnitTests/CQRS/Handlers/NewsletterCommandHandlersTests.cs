using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class NewsletterCommandHandlersTests
{
    private readonly IMailerLiteClient _mailerLiteClient = Substitute.For<IMailerLiteClient>();
    private readonly ILogger<SubscribeNewsletterCommand> _subLogger = Substitute.For<ILogger<SubscribeNewsletterCommand>>();
    private readonly ILogger<UnsubscribeNewsletterCommand> _unsubLogger = Substitute.For<ILogger<UnsubscribeNewsletterCommand>>();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("user@localhost")]
    public async Task Subscribe_WhenEmailIsInvalid_ReturnsFalseWithoutCallingMailerLite(string invalidEmail)
    {
        // Arrange
        var command = new SubscribeNewsletterCommand(invalidEmail, "Jan");

        // Act
        var result = await SubscribeNewsletterCommandHandler.Handle(
            command, _mailerLiteClient, _subLogger, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("poprawny adres e-mail");
        await _mailerLiteClient.DidNotReceive().SubscribeAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenEmailIsValidAndClientSucceeds_ReturnsTrue()
    {
        // Arrange
        _mailerLiteClient.SubscribeAsync("jan@example.com", "Jan Kowalski", Arg.Any<CancellationToken>())
            .Returns(true);
        var command = new SubscribeNewsletterCommand("  jan@example.com  ", "Jan Kowalski");

        // Act
        var result = await SubscribeNewsletterCommandHandler.Handle(
            command, _mailerLiteClient, _subLogger, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        await _mailerLiteClient.Received(1).SubscribeAsync("jan@example.com", "Jan Kowalski", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_WhenClientThrowsException_ReturnsFalseGracefully()
    {
        // Arrange
        _mailerLiteClient.SubscribeAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<bool>(_ => throw new HttpRequestException("API down"));
        var command = new SubscribeNewsletterCommand("jan@example.com", null);

        // Act
        var result = await SubscribeNewsletterCommandHandler.Handle(
            command, _mailerLiteClient, _subLogger, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("błąd serwera");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-email")]
    public async Task Unsubscribe_WhenEmailIsInvalid_ReturnsFalseWithoutCallingMailerLite(string invalidEmail)
    {
        // Arrange
        var command = new UnsubscribeNewsletterCommand(invalidEmail);

        // Act
        var result = await UnsubscribeNewsletterCommandHandler.Handle(
            command, _mailerLiteClient, _unsubLogger, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        await _mailerLiteClient.DidNotReceive().UnsubscribeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unsubscribe_WhenEmailIsValidAndClientSucceeds_ReturnsTrue()
    {
        // Arrange
        _mailerLiteClient.UnsubscribeAsync("jan@example.com", Arg.Any<CancellationToken>())
            .Returns(true);
        var command = new UnsubscribeNewsletterCommand(" jan@example.com ");

        // Act
        var result = await UnsubscribeNewsletterCommandHandler.Handle(
            command, _mailerLiteClient, _unsubLogger, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        await _mailerLiteClient.Received(1).UnsubscribeAsync("jan@example.com", Arg.Any<CancellationToken>());
    }
}

