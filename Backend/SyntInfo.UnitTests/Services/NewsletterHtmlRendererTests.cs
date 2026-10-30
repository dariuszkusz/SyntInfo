using SyntInfo.Domain.Entities;
using SyntInfo.Infrastructure.Services.Newsletter;

namespace SyntInfo.UnitTests.Services;

public class NewsletterHtmlRendererTests
{
    [Fact]
    public void Render_EscapesHtmlUpgradesHttpImageUrlsAndRendersRegionBadges()
    {
        // Arrange
        var articles = new List<NewsletterArticle>
        {
            new(
                Title: "Tytuł <script>alert(1)</script>",
                SummaryText: "Treść z ampersandem & cudzysłowem",
                CategoryName: "Polityka",
                Region: SourceRegion.Poland,
                PublishedAtUtc: new DateTime(2026, 10, 30, 8, 30, 0, DateTimeKind.Utc),
                ImageUrl: "http://insecure.example.com/photo.jpg",
                SourceUrls: new[] { "https://www.pap.pl/artykul/1" }),
            new(
                Title: "Pozytywna wiadomość ze świata nauki",
                SummaryText: "Odkrycie naukowe.",
                CategoryName: "Nauka",
                Region: SourceRegion.Positive,
                PublishedAtUtc: new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc),
                ImageUrl: null,
                SourceUrls: new[] { "https://reuters.com/science" })
        };

        // Act
        var html = NewsletterHtmlRenderer.Render(articles, "https://infoskrot.pl/");

        // Assert
        html.Should().Contain("<!DOCTYPE html>");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("https://insecure.example.com/photo.jpg");
        html.Should().NotContain("http://insecure.example.com/photo.jpg");
        html.Should().Contain("Pozytywne");
        html.Should().Contain("Polska");
        html.Should().Contain("2 wiadomości");
    }
}

