using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class SendTestSocialDraftCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IRepository<NewsArticle> _articleRepo = Substitute.For<IRepository<NewsArticle>>();
    private readonly IRepository<SocialPostDraft> _draftRepo = Substitute.For<IRepository<SocialPostDraft>>();
    private readonly ITelegramNotificationService _telegramService = Substitute.For<ITelegramNotificationService>();
    private readonly ILogger<SendTestSocialDraftCommand> _logger = Substitute.For<ILogger<SendTestSocialDraftCommand>>();

    public SendTestSocialDraftCommandHandlerTests()
    {
        _uow.Repository<NewsArticle>().Returns(_articleRepo);
        _uow.Repository<SocialPostDraft>().Returns(_draftRepo);
    }

    [Fact]
    public async Task Handle_WhenNoArticlesExist_CreatesSampleArticleAndDraftAndSendsToTelegram()
    {
        // Arrange
        _articleRepo.Query().Returns(new List<NewsArticle>().BuildMock());
        _telegramService.SendDraftToAdminAsync(Arg.Any<SocialPostDraft>(), Arg.Any<NewsArticle>(), Arg.Any<CancellationToken>())
            .Returns(789L);

        var command = new SendTestSocialDraftCommand(SocialPlatformType.Wykop);

        // Act
        var result = await SendTestSocialDraftCommandHandler.Handle(
            command, _uow, _telegramService, _logger, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TelegramMessageId.Should().Be(789L);
        result.Platform.Should().Be("Wykop");
        await _articleRepo.Received(1).AddAsync(Arg.Any<NewsArticle>(), Arg.Any<CancellationToken>());
        await _draftRepo.Received(1).AddAsync(Arg.Any<SocialPostDraft>(), Arg.Any<CancellationToken>());
        _draftRepo.Received(1).Update(Arg.Is<SocialPostDraft>(d => d.TelegramMessageId == 789L));
    }

    [Fact]
    public async Task Handle_WhenTelegramThrowsException_ReturnsFailureResult()
    {
        // Arrange
        var existingArticle = new NewsArticle { Id = Guid.NewGuid(), Title = "Test", SummaryText = "Opis" };
        _articleRepo.Query().Returns(new List<NewsArticle> { existingArticle }.BuildMock());
        _telegramService.SendDraftToAdminAsync(Arg.Any<SocialPostDraft>(), Arg.Any<NewsArticle>(), Arg.Any<CancellationToken>())
            .Returns<long>(_ => throw new InvalidOperationException("Brak połączenia z Telegramem"));

        var command = new SendTestSocialDraftCommand(SocialPlatformType.Hejto);

        // Act
        var result = await SendTestSocialDraftCommandHandler.Handle(
            command, _uow, _telegramService, _logger, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Brak połączenia z Telegramem");
    }
}

