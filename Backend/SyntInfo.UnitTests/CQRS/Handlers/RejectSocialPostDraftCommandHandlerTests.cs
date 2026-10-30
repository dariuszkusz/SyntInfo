using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class RejectSocialPostDraftCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IRepository<SocialPostDraft> _draftRepo = Substitute.For<IRepository<SocialPostDraft>>();
    private readonly ITelegramNotificationService _telegramService = Substitute.For<ITelegramNotificationService>();
    private readonly ILogger<RejectSocialPostDraftCommand> _logger = Substitute.For<ILogger<RejectSocialPostDraftCommand>>();

    public RejectSocialPostDraftCommandHandlerTests()
    {
        _uow.Repository<SocialPostDraft>().Returns(_draftRepo);
    }

    [Fact]
    public async Task Handle_WhenDraftExistsAndCanBeRejected_MarksRejectedAndUpdatesTelegram()
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var draft = new SocialPostDraft
        {
            Id = draftId,
            Status = SocialPostStatus.Draft,
            TelegramMessageId = 1234
        };
        _draftRepo.Query().Returns(new List<SocialPostDraft> { draft }.BuildMock());

        var command = new RejectSocialPostDraftCommand(draftId);

        // Act
        await RejectSocialPostDraftCommandHandler.Handle(
            command, _uow, _telegramService, _logger, CancellationToken.None);

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Rejected);
        _draftRepo.Received(1).Update(draft);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _telegramService.Received(1).UpdateDraftMessageAsync(
            1234, draft, "🚫 Odrzucono publikację", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenDraftCannotBeRejected_DoesNotSaveOrNotify()
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var draft = new SocialPostDraft
        {
            Id = draftId,
            Status = SocialPostStatus.Published
        };
        _draftRepo.Query().Returns(new List<SocialPostDraft> { draft }.BuildMock());

        var command = new RejectSocialPostDraftCommand(draftId, 555);

        // Act
        await RejectSocialPostDraftCommandHandler.Handle(
            command, _uow, _telegramService, _logger, CancellationToken.None);

        // Assert
        draft.Status.Should().Be(SocialPostStatus.Published);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _telegramService.DidNotReceive().UpdateDraftMessageAsync(
            Arg.Any<long>(), Arg.Any<SocialPostDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

