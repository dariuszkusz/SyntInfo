using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class WykopAuthCommandHandlersTests
{
    private readonly IWykopClient _wykopClient = Substitute.For<IWykopClient>();
    private readonly ILogger<GetWykopConnectUrlCommand> _connectLogger = Substitute.For<ILogger<GetWykopConnectUrlCommand>>();
    private readonly ILogger<SaveWykopUserTokensCommand> _saveLogger = Substitute.For<ILogger<SaveWykopUserTokensCommand>>();

    [Fact]
    public async Task GetConnectUrl_WhenClientSucceeds_ReturnsSuccessWithConnectUrl()
    {
        // Arrange
        _wykopClient.GetConnectUrlAsync(Arg.Any<CancellationToken>())
            .Returns("https://wykop.pl/connect/abc");

        // Act
        var result = await WykopAuthCommandHandler.Handle(
            new GetWykopConnectUrlCommand(), _wykopClient, _connectLogger, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ConnectUrl.Should().Be("https://wykop.pl/connect/abc");
    }

    [Fact]
    public async Task SaveUserTokens_WhenTokenIsEmpty_ReturnsValidationFailure()
    {
        // Act
        var result = await WykopAuthCommandHandler.Handle(
            new SaveWykopUserTokensCommand("   ", "refresh"), _wykopClient, _saveLogger, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("nie może być puste");
        await _wykopClient.DidNotReceive().SaveUserTokensAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveUserTokens_WhenTokenIsValid_CallsWykopClientAndReturnsSuccess()
    {
        // Act
        var result = await WykopAuthCommandHandler.Handle(
            new SaveWykopUserTokensCommand("user-jwt", "refresh-jwt"), _wykopClient, _saveLogger, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        await _wykopClient.Received(1).SaveUserTokensAsync("user-jwt", "refresh-jwt", Arg.Any<CancellationToken>());
    }
}

