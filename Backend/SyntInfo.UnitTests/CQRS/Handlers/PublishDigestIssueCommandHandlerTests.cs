using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SyntInfo.Application.CQRS.Commands;
using SyntInfo.Application.CQRS.Handlers;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Domain.Interfaces;
using Wolverine;
using Xunit;

namespace SyntInfo.UnitTests.CQRS.Handlers;

public class PublishDigestIssueCommandHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly INewsArticleRepository _articleRepo = Substitute.For<INewsArticleRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ISocialPostGenerator _postGenerator = Substitute.For<ISocialPostGenerator>();
    private readonly IWykopClient _wykopClient = Substitute.For<IWykopClient>();
    private readonly IHejtoClient _hejtoClient = Substitute.For<IHejtoClient>();
    private readonly IMailerLiteClient _mailerLiteClient = Substitute.For<IMailerLiteClient>();
    private readonly ITelegramNotificationService _telegramService = Substitute.For<ITelegramNotificationService>();
    private readonly ITelegramCurationStateService _telegramState = Substitute.For<ITelegramCurationStateService>();
    private readonly IOptions<CurationOptions> _curationOptions = Options.Create(new CurationOptions { MaxArticlesPerRun = 5 });
    private readonly IOptions<HejtoOptions> _hejtoOptions = Options.Create(new HejtoOptions { PublishArticlesSeparately = false });
    private readonly IOptions<MailerLiteOptions> _mailerLiteOptions = Options.Create(new MailerLiteOptions { Enabled = false });
    private readonly ILogger<PublishDigestIssueCommand> _logger = Substitute.For<ILogger<PublishDigestIssueCommand>>();

    public PublishDigestIssueCommandHandlerTests()
    {
        _uow.NewsArticles.Returns(_articleRepo);
    }

    [Fact]
    public async Task Handle_WhenPublishingDigest_MarksUnselectedArticlesAsInactiveAndArchived()
    {
        // Arrange
        var selectedArticle = new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = "Wybrany artykuł",
            IsSelectedForDigest = true,
            IsActive = true,
            DigestPublishedAt = null
        };

        var unselectedArticle = new NewsArticle
        {
            Id = Guid.NewGuid(),
            Title = "Odrzucony artykuł",
            IsSelectedForDigest = false,
            IsActive = true,
            DigestPublishedAt = null
        };

        _articleRepo.GetSelectedForDigestAsync(Arg.Any<CancellationToken>())
            .Returns(new List<NewsArticle> { selectedArticle });

        var allArticles = new List<NewsArticle> { selectedArticle, unselectedArticle };
        _articleRepo.Query().Returns(allArticles.BuildMock());

        _postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Wykop, Arg.Any<IReadOnlyList<NewsArticle>>(), Arg.Any<CancellationToken>())
            .Returns("Wykop digest content");
        _postGenerator.GenerateDigestContentForPlatformAsync(SocialPlatformType.Hejto, Arg.Any<IReadOnlyList<NewsArticle>>(), Arg.Any<CancellationToken>())
            .Returns("Hejto digest content");

        var command = new PublishDigestIssueCommand();

        // Act
        await PublishDigestIssueCommandHandler.Handle(
            command,
            _uow,
            _bus,
            _postGenerator,
            _wykopClient,
            _hejtoClient,
            _mailerLiteClient,
            _telegramService,
            _telegramState,
            _curationOptions,
            _hejtoOptions,
            _mailerLiteOptions,
            _logger,
            CancellationToken.None);

        // Assert
        Assert.NotNull(selectedArticle.DigestPublishedAt);
        Assert.False(selectedArticle.IsSelectedForDigest);
        Assert.True(selectedArticle.IsActive); // Wybrany pozostaje aktywny

        Assert.NotNull(unselectedArticle.DigestPublishedAt);
        Assert.False(unselectedArticle.IsSelectedForDigest);
        Assert.False(unselectedArticle.IsActive); // Odrzucony zostaje ukryty w aplikacji / API

        _articleRepo.Received().Update(selectedArticle);
        _articleRepo.Received().Update(unselectedArticle);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
