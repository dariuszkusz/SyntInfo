using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Entities;
using SyntInfo.Infrastructure.Services;

namespace SyntInfo.UnitTests.Services;

public class SocialPostGeneratorTests
{
    private readonly IGoogleAiStudioClient _aiClient = Substitute.For<IGoogleAiStudioClient>();
    private readonly ILogger<SocialPostGenerator> _logger = Substitute.For<ILogger<SocialPostGenerator>>();

    [Fact]
    public async Task GenerateContentForPlatformAsync_WhenAiReturnsMarkdownJson_ExtractsEssenceField()
    {
        // Arrange
        _aiClient.GenerateSummaryFromFactsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("```json\n{\"essence\": \"Krótki wpis na Wykop #wiadomosci #technologia\"}\n```");

        var generator = new SocialPostGenerator(_aiClient, _logger);
        var article = new NewsArticle
        {
            Title = "Nowy procesor kwantowy",
            SummaryText = "Podsumowanie artykułu",
            SourceUrls = new List<string> { "https://example.com/quantum" },
            Category = new NewsCategory { Name = "Technologia" }
        };

        // Act
        var content = await generator.GenerateContentForPlatformAsync(SocialPlatformType.Wykop, article);

        // Assert
        content.Should().Be("Krótki wpis na Wykop #wiadomosci #technologia");
    }

    [Theory]
    [InlineData(SocialPlatformType.Wykop)]
    [InlineData(SocialPlatformType.Hejto)]
    [InlineData(SocialPlatformType.Facebook)]
    [InlineData(SocialPlatformType.Newsletter)]
    public async Task GenerateContentForPlatformAsync_WhenAiThrowsException_ReturnsFallbackTemplate(SocialPlatformType platform)
    {
        // Arrange
        _aiClient.GenerateSummaryFromFactsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("AI niedostępne"));

        var generator = new SocialPostGenerator(_aiClient, _logger);
        var article = new NewsArticle
        {
            Title = "Ważny news dnia",
            SummaryText = "Treść podsumowania testowego artykułu.",
            SourceUrls = new List<string> { "https://example.com/news" },
            Category = new NewsCategory { Name = "Gospodarka" }
        };

        // Act
        var content = await generator.GenerateContentForPlatformAsync(platform, article);

        // Assert
        content.Should().NotBeNullOrWhiteSpace();
        content.Should().Contain("Ważny news dnia");
        content.Should().Contain("https://example.com/news");
    }

    [Theory]
    [InlineData(SocialPlatformType.Wykop, "InfoSkrót — Wydanie z")]
    [InlineData(SocialPlatformType.Hejto, "#InfoSkrót — Wydanie z")]
    [InlineData(SocialPlatformType.Facebook, "InfoSkrót — Przegląd wiadomości z")]
    [InlineData(SocialPlatformType.Newsletter, "<!DOCTYPE html>")]
    public async Task GenerateDigestContentForPlatformAsync_ReturnsFormattedDigestForEachPlatform(
        SocialPlatformType platform, string expectedSubstring)
    {
        // Arrange
        var generator = new SocialPostGenerator(_aiClient, _logger);
        var articles = new List<NewsArticle>
        {
            new()
            {
                Title = "Artykuł pierwszy",
                SummaryText = "Podsumowanie pierwszego artykułu.",
                SourceUrls = new List<string> { "https://example.com/1" },
                ImageUrl = "https://example.com/img1.jpg"
            }
        };

        // Act
        var digest = await generator.GenerateDigestContentForPlatformAsync(platform, articles);

        // Assert
        digest.Should().Contain(expectedSubstring);
        digest.Should().Contain("Artykuł pierwszy");
    }
}

