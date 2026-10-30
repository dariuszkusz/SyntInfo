using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SyntInfo.Application.Interfaces;
using SyntInfo.Application.Options;
using SyntInfo.Domain.Entities;
using SyntInfo.Infrastructure.Services;
using SyntInfo.UnitTests.TestHelpers;

namespace SyntInfo.UnitTests.Services;

public class GoogleAiStudioClientTests
{
    private readonly IOpenRouterClient _openRouterClient = Substitute.For<IOpenRouterClient>();
    private readonly ILogger<GoogleAiStudioClient> _logger = Substitute.For<ILogger<GoogleAiStudioClient>>();

    [Fact]
    public async Task SelectTopArticlesIndexesAsync_WhenApiKeyIsPlaceholder_FallsBackToOpenRouterImmediately()
    {
        // Arrange
        var options = Options.Create(new GoogleAiStudioOptions { ApiKey = "PLACEHOLDER_GOOGLE_KEY" });
        _openRouterClient.SelectTopArticlesIndexesAsync("[]", 3, SourceRegion.Poland, Arg.Any<CancellationToken>())
            .Returns(new List<int> { 0, 2 });

        var client = new GoogleAiStudioClient(new HttpClient(), _openRouterClient, options, _logger);

        // Act
        var indexes = await client.SelectTopArticlesIndexesAsync("[]", 3, SourceRegion.Poland);

        // Assert
        indexes.Should().Equal(0, 2);
        await _openRouterClient.Received(1).SelectTopArticlesIndexesAsync("[]", 3, SourceRegion.Poland, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateFactsAsync_WhenPrimaryGeminiModelSucceeds_ReturnsGeminiTextWithoutCallingOpenRouter()
    {
        // Arrange
        var options = Options.Create(new GoogleAiStudioOptions
        {
            ApiKey = "valid_gemini_key",
            Model = "gemini-3.5-flash"
        });

        var geminiJson = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { text = "{\"facts\":[\"Fakt 1\"]}" } }
                    }
                }
            }
        });

        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(geminiJson)
        }));

        var client = new GoogleAiStudioClient(new HttpClient(handler), _openRouterClient, options, _logger);

        // Act
        var result = await client.GenerateFactsAsync("RSS", "Search");

        // Assert
        result.Should().Be("{\"facts\":[\"Fakt 1\"]}");
        await _openRouterClient.DidNotReceive().GenerateFactsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateSummaryFromFactsAsync_WhenGeminiReturnsError_FallsBackToOpenRouter()
    {
        // Arrange
        var options = Options.Create(new GoogleAiStudioOptions
        {
            ApiKey = "valid_gemini_key",
            Model = "gemini-3.5-flash",
            FallbackModel = "gemini-3.5-flash" // same model so no secondary retry delay
        });

        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Invalid request")
        }));

        _openRouterClient.GenerateSummaryFromFactsAsync("{\"facts\":[]}", Arg.Any<CancellationToken>())
            .Returns("{\"title\":\"Tytuł z OpenRouter\"}");

        var client = new GoogleAiStudioClient(new HttpClient(handler), _openRouterClient, options, _logger);

        // Act
        var result = await client.GenerateSummaryFromFactsAsync("{\"facts\":[]}");

        // Assert
        result.Should().Be("{\"title\":\"Tytuł z OpenRouter\"}");
        await _openRouterClient.Received(1).GenerateSummaryFromFactsAsync("{\"facts\":[]}", Arg.Any<CancellationToken>());
    }
}

