using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Infrastructure.Services;
using SyntInfo.Infrastructure.Services.Publishers;

namespace SyntInfo.UnitTests.Services;

public class SocialPublishersTests
{
    [Fact]
    public async Task WykopPublisher_WithImageUrl_UploadsPhotoAndCreatesEntry()
    {
        // Arrange
        var wykopClient = Substitute.For<IWykopClient>();
        wykopClient.UploadPhotoAsync("https://example.com/pic.jpg", Arg.Any<CancellationToken>())
            .Returns("photo-key-1");
        wykopClient.CreateEntryAsync("Treść wpisu", "photo-key-1", Arg.Any<CancellationToken>())
            .Returns("entry-100");

        var publisher = new WykopPublisher(wykopClient);
        var draft = new SocialPostDraft { Content = "Treść wpisu" };
        var article = new NewsArticle { ImageUrl = "https://example.com/pic.jpg" };

        // Act
        var result = await publisher.PublishAsync(draft, article);

        // Assert
        result.Success.Should().BeTrue();
        result.ExternalPostId.Should().Be("entry-100");
    }

    [Fact]
    public async Task HejtoPublisher_WithImageUrl_UploadsImageAndCreatesPost()
    {
        // Arrange
        var hejtoClient = Substitute.For<IHejtoClient>();
        hejtoClient.UploadImageAsync("https://example.com/pic.jpg", Arg.Any<CancellationToken>())
            .Returns("uuid-1");
        hejtoClient.CreatePostAsync("Treść Hejto", null, "uuid-1", Arg.Any<CancellationToken>())
            .Returns("slug-100");

        var publisher = new HejtoPublisher(hejtoClient);
        var draft = new SocialPostDraft { Content = "Treść Hejto" };
        var article = new NewsArticle { ImageUrl = "https://example.com/pic.jpg" };

        // Act
        var result = await publisher.PublishAsync(draft, article);

        // Assert
        result.Success.Should().BeTrue();
        result.ExternalPostId.Should().Be("slug-100");
    }

    [Fact]
    public async Task FacebookPublisher_PublishesPagePostWithPrimarySourceLink()
    {
        // Arrange
        var fbClient = Substitute.For<IFacebookClient>();
        fbClient.PublishPagePostAsync("Post FB", "https://example.com/source", Arg.Any<CancellationToken>())
            .Returns("fb-123");

        var publisher = new FacebookPublisher(fbClient);
        var draft = new SocialPostDraft { Content = "Post FB" };
        var article = new NewsArticle { SourceUrls = new List<string> { "https://example.com/source" } };

        // Act
        var result = await publisher.PublishAsync(draft, article);

        // Assert
        result.Success.Should().BeTrue();
        result.ExternalPostId.Should().Be("fb-123");
    }

    [Fact]
    public async Task NewsletterPublisher_RendersHtmlAndSendsCampaign()
    {
        // Arrange
        var mailerLite = Substitute.For<IMailerLiteClient>();
        mailerLite.SendNewsletterCampaignAsync("Tytuł artykułu", Arg.Is<string>(h => h.Contains("<!DOCTYPE html>")), "Skrót", Arg.Any<CancellationToken>())
            .Returns("camp-555");

        var publisher = new NewsletterPublisher(mailerLite);
        var draft = new SocialPostDraft { Content = "Skrót" };
        var article = new NewsArticle { Title = "Tytuł artykułu", SummaryText = "Skrót" };

        // Act
        var result = await publisher.PublishAsync(draft, article);

        // Assert
        result.Success.Should().BeTrue();
        result.ExternalPostId.Should().Be("camp-555");
    }

    [Fact]
    public void SocialPublisherFactory_ReturnsMatchingPublisherByPlatformType()
    {
        // Arrange
        var wykopPub = new WykopPublisher(Substitute.For<IWykopClient>());
        var hejtoPub = new HejtoPublisher(Substitute.For<IHejtoClient>());
        var factory = new SocialPublisherFactory(new ISocialPlatformPublisher[] { wykopPub, hejtoPub });

        // Act & Assert
        factory.GetPublisher(SocialPlatformType.Wykop).Should().BeSameAs(wykopPub);
        factory.GetPublisher(SocialPlatformType.Hejto).Should().BeSameAs(hejtoPub);
        factory.GetPublisher(SocialPlatformType.Facebook).Should().BeNull();
    }
}

