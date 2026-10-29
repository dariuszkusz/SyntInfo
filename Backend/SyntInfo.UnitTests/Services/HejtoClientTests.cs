using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SyntInfo.Application.Options;
using SyntInfo.Infrastructure.Services;

namespace SyntInfo.UnitTests.Services;

public class HejtoClientTests
{
    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; }

        public TestHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            Handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Handler(request);
        }
    }

    [Fact]
    public async Task UploadImageAsync_WhenUseMockIsTrue_ReturnsMockUuid()
    {
        // Arrange
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new HejtoOptions { UseMock = true });
        var logger = Substitute.For<ILogger<HejtoClient>>();

        var client = new HejtoClient(httpClientFactory, memoryCache, options, logger);

        // Act
        var uuid = await client.UploadImageAsync("https://example.com/photo.jpg");

        // Assert
        uuid.Should().NotBeNullOrWhiteSpace();
        uuid.Should().StartWith("hejto-mock-img-");
    }

    [Fact]
    public async Task UploadImageAsync_WhenValidUrl_DownloadsAndUploadsToHejtoApi()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new HejtoOptions
        {
            UseMock = false,
            BaseUrl = "https://api.hejto.pl/",
            Token = "valid_static_token"
        });

        bool uploadEndpointCalled = false;

        var handler = new TestHttpMessageHandler(async request =>
        {
            var uri = request.RequestUri!.ToString();

            if (uri == "https://example.com/news.jpg")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 })
                };
            }

            if (uri.Contains("uploads?target=post&source=file"))
            {
                uploadEndpointCalled = true;
                request.Headers.Authorization!.Parameter.Should().Be("valid_static_token");
                request.Content.Should().BeOfType<MultipartFormDataContent>();

                var respJson = JsonSerializer.Serialize(new { uuid = "uploaded-img-uuid-123" });
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(respJson)
                };
            }

            await Task.CompletedTask;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var logger = Substitute.For<ILogger<HejtoClient>>();
        var client = new HejtoClient(httpClientFactory, memoryCache, options, logger);

        // Act
        var uuid = await client.UploadImageAsync("https://example.com/news.jpg");

        // Assert
        uploadEndpointCalled.Should().BeTrue();
        uuid.Should().Be("uploaded-img-uuid-123");
    }

    [Fact]
    public async Task CreatePostAsync_WithImageUuid_IncludesImagesArrayInPayload()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new HejtoOptions
        {
            UseMock = false,
            BaseUrl = "https://api.hejto.pl/",
            Token = "valid_static_token",
            DefaultCommunity = "wiadomosci-polska"
        });

        string? capturedPayload = null;

        var handler = new TestHttpMessageHandler(async request =>
        {
            if (request.RequestUri!.ToString().EndsWith("/posts"))
            {
                capturedPayload = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { slug = "test-post-slug" }))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var logger = Substitute.For<ILogger<HejtoClient>>();
        var client = new HejtoClient(httpClientFactory, memoryCache, options, logger);

        // Act
        var postId = await client.CreatePostAsync("Treść wpisu #wiadomosci", null, "img-uuid-999");

        // Assert
        postId.Should().Be("test-post-slug");
        capturedPayload.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedPayload!);
        doc.RootElement.GetProperty("images")[0].GetProperty("uuid").GetString().Should().Be("img-uuid-999");
        doc.RootElement.GetProperty("images")[0].GetProperty("position").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task CreatePostAsync_WithoutImageUuid_DoesNotIncludeImagesArrayInPayload()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new HejtoOptions
        {
            UseMock = false,
            BaseUrl = "https://api.hejto.pl/",
            Token = "valid_static_token",
            DefaultCommunity = "wiadomosci-polska"
        });

        string? capturedPayload = null;

        var handler = new TestHttpMessageHandler(async request =>
        {
            if (request.RequestUri!.ToString().EndsWith("/posts"))
            {
                capturedPayload = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { slug = "digest-post-slug" }))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var logger = Substitute.For<ILogger<HejtoClient>>();
        var client = new HejtoClient(httpClientFactory, memoryCache, options, logger);

        // Act
        var postId = await client.CreatePostAsync("Zbiorcze wydanie #wiadomosci");

        // Assert
        postId.Should().Be("digest-post-slug");
        capturedPayload.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedPayload!);
        doc.RootElement.TryGetProperty("images", out _).Should().BeFalse();
    }
}
