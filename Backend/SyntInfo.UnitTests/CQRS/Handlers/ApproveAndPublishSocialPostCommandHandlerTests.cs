using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Models.Social;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class ApproveAndPublishSocialPostCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IRepository<SocialPostDraft> _draftRepo = Substitute.For<IRepository<SocialPostDraft>>();
    private readonly ISocialPublisherFactory _publisherFactory = Substitute.For<ISocialPublisherFactory>();
    private readonly ITelegramNotificationService _telegramService = Substitute.For<ITelegramNotificationService>();
    private readonly ILogger<ApproveAndPublishSocialPostCommand> _logger = Substitute.For<ILogger<ApproveAndPublishSocialPostCommand>>();

    public ApproveAndPublishSocialPostCommandHandlerTests()
    {
        _uow.Repository<SocialPostDraft>().Returns(_draftRepo);
    }

    [Fact]
    public async Task Handle_WhenDraftNotFound_DoesNotSaveOrPublish()
    {
        // Arrange
        var drafts = new List<SocialPostDraft>();
        _draftRepo.Query().Returns(drafts.BuildMock());

        var command = new ApproveAndPublishSocialPostCommand(Guid.NewGuid(), 100);

        // Act
        await ApproveAndPublishSocialPostCommandHandler.Handle(
            command, _uow, _publisherFactory, _telegramService, _logger, CancellationToken.None);

        // Assert
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _publisherFactory.DidNotReceive().GetPublisher(Arg.Any<SocialPlatformType>());
    }

    [Fact]
    public async Task Handle_WhenDraftCannotBeApproved_DoesNotPublish()
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var draft = new SocialPostDraft
        {
            Id = draftId,
            Status = SocialPostStatus.Published,
            NewsArticle = new NewsArticle { Id = Guid.NewGuid(), Title = "Artykuł" }
        };
        _draftRepo.Query().Returns(new List<SocialPostDraft> { draft }.BuildMock());

        var command = new ApproveAndPublishSocialPostCommand(draftId, 100);

        // Act
        await ApproveAndPublishSocialPostCommandHandler.Handle(
            command, _uow, _publisherFactory, _telegramService, _logger, CancellationToken.None);

        // Assert
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _publisherFactory.DidNotReceive().GetPublisher(Arg.Any<SocialPlatformType>());
    }

    [Fact]
    public async Task Handle_WhenPublisherNotFound_MarksDraftFailedAndNotifiesTelegram()
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var draft = new SocialPostDraft
        {
            Id = draftId,
            PlatformType = SocialPlatformType.Wykop,
            Status = SocialPostStatus.Draft,
            TelegramMessageId = 321,
            NewsArticle = new NewsArticle { Id = Guid.NewGuid(), Title = "Artykuł" }
        };
        _draftRepo.Query().Returns(new List<SocialPostDraft> { draft }.BuildMock());
        _publisherFactory.GetPublisher(SocialPlatformType.Wykop).Returns((ISocialPlatformPublisher?)null);

        var command = new ApproveAndPublishSocialPostCommand(draftId);

        // Act
        await ApproveAndPublishSocialPostCommandHandler.Handle(
            command, _uow, _publisherFactory, _telegramService, _logger, CancellationToken.None);

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Failed);
        draft.ErrorMessage.Should().Contain("Brak zarejestrowanego modułu publikacji");
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _telegramService.Received(1).UpdateDraftMessageAsync(
            321, draft, Arg.Is<string>(s => s.Contains("Błąd konfiguracji")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenPublishSucceeds_MarksDraftPublishedAndUpdatesTelegram()
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var article = new NewsArticle { Id = Guid.NewGuid(), Title = "Artykuł" };
        var draft = new SocialPostDraft
        {
            Id = draftId,
            PlatformType = SocialPlatformType.Hejto,
            Status = SocialPostStatus.Draft,
            NewsArticle = article
        };
        _draftRepo.Query().Returns(new List<SocialPostDraft> { draft }.BuildMock());

        var publisher = Substitute.For<ISocialPlatformPublisher>();
        publisher.PublishAsync(draft, article, Arg.Any<CancellationToken>())
            .Returns(PublishResult.Succeeded("hejto-post-99"));
        _publisherFactory.GetPublisher(SocialPlatformType.Hejto).Returns(publisher);

        var command = new ApproveAndPublishSocialPostCommand(draftId, TelegramMessageId: 888);

        // Act
        await ApproveAndPublishSocialPostCommandHandler.Handle(
            command, _uow, _publisherFactory, _telegramService, _logger, CancellationToken.None);

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Published);
        draft.ExternalPostId.Should().Be("hejto-post-99");
        draft.PublishedAt.Should().NotBeNull();
        await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _telegramService.Received(1).UpdateDraftMessageAsync(
            888, draft, Arg.Is<string>(s => s.Contains("Opublikowano na Hejto") && s.Contains("hejto-post-99")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenPublishFails_MarksDraftFailedAndUpdatesTelegram()
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var article = new NewsArticle { Id = Guid.NewGuid(), Title = "Artykuł" };
        var draft = new SocialPostDraft
        {
            Id = draftId,
            PlatformType = SocialPlatformType.Facebook,
            Status = SocialPostStatus.Draft,
            NewsArticle = article
        };
        _draftRepo.Query().Returns(new List<SocialPostDraft> { draft }.BuildMock());

        var publisher = Substitute.For<ISocialPlatformPublisher>();
        publisher.PublishAsync(draft, article, Arg.Any<CancellationToken>())
            .Returns(PublishResult.Failed("Invalid OAuth token"));
        _publisherFactory.GetPublisher(SocialPlatformType.Facebook).Returns(publisher);

        var command = new ApproveAndPublishSocialPostCommand(draftId, TelegramMessageId: 444);

        // Act
        await ApproveAndPublishSocialPostCommandHandler.Handle(
            command, _uow, _publisherFactory, _telegramService, _logger, CancellationToken.None);

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Failed);
        draft.ErrorMessage.Should().Be("Invalid OAuth token");
        await _telegramService.Received(1).UpdateDraftMessageAsync(
            444, draft, Arg.Is<string>(s => s.Contains("Invalid OAuth token")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenPublisherThrowsException_MarksDraftFailedAndUpdatesTelegram()
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var article = new NewsArticle { Id = Guid.NewGuid(), Title = "Artykuł" };
        var draft = new SocialPostDraft
        {
            Id = draftId,
            PlatformType = SocialPlatformType.Wykop,
            Status = SocialPostStatus.Draft,
            NewsArticle = article
        };
        _draftRepo.Query().Returns(new List<SocialPostDraft> { draft }.BuildMock());

        var publisher = Substitute.For<ISocialPlatformPublisher>();
        publisher.PublishAsync(draft, article, Arg.Any<CancellationToken>())
            .Returns<PublishResult>(_ => throw new HttpRequestException("Timeout API"));
        _publisherFactory.GetPublisher(SocialPlatformType.Wykop).Returns(publisher);

        var command = new ApproveAndPublishSocialPostCommand(draftId, TelegramMessageId: 999);

        // Act
        await ApproveAndPublishSocialPostCommandHandler.Handle(
            command, _uow, _publisherFactory, _telegramService, _logger, CancellationToken.None);

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Failed);
        draft.ErrorMessage.Should().Contain("Timeout API");
        await _telegramService.Received(1).UpdateDraftMessageAsync(
            999, draft, Arg.Is<string>(s => s.Contains("Błąd krytyczny: Timeout API")), Arg.Any<CancellationToken>());
    }
}

