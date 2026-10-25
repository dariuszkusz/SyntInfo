using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SyntInfo.Application.Options;
using SyntInfo.Infrastructure.Services;

namespace SyntInfo.UnitTests.Services;

public class WykopClientTests
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
    public async Task CreateEntryAsync_WhenUseMockIsTrue_ReturnsMockString()
    {
        // Arrange
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new WykopOptions { UseMock = true });
        var logger = Substitute.For<ILogger<WykopClient>>();

        var client = new WykopClient(httpClientFactory, memoryCache, options, logger);

        // Act
        var result = await client.CreateEntryAsync("Test content");

        // Assert
        result.Should().StartWith("wykop-mo");
    }

    [Fact]
    public async Task CreateEntryAsync_WhenFirstCallReturns403_RefreshesTokenAndRetries()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new WykopOptions
        {
            UseMock = false,
            BaseUrl = "https://wykop.pl/api/v3/",
            Key = "test_key",
            Secret = "test_secret",
            RefreshToken = "test_refresh_token"
        });

        // Set initial user token in memory cache
        memoryCache.Set("wykop_user_oauth_token", "expired_token");
        memoryCache.Set("wykop_user_refresh_token", "test_refresh_token");

        int entriesCallCount = 0;
        bool refreshCalled = false;

        var handler = new TestHttpMessageHandler(request =>
        {
            var uri = request.RequestUri!.ToString();

            // 1. App token /auth call (if triggered by refresh-token)
            if (uri.Contains("auth"))
            {
                var authResp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        data = new { token = "new_app_token" }
                    }))
                };
                return Task.FromResult(authResp);
            }

            // 2. /refresh-token call
            if (uri.Contains("refresh-token"))
            {
                refreshCalled = true;
                var refreshResp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        data = new
                        {
                            token = "refreshed_user_token",
                            refresh_token = "next_refresh_token"
                        }
                    }))
                };
                return Task.FromResult(refreshResp);
            }

            // 3. /entries call
            if (uri.Contains("entries"))
            {
                entriesCallCount++;
                if (entriesCallCount == 1)
                {
                    // First attempt with expired token fails with 403
                    var forbiddenResp = new HttpResponseMessage(HttpStatusCode.Forbidden)
                    {
                        Content = new StringContent("{\"code\":403,\"error\":{\"message\":\"Authentication required\",\"key\":2}}")
                    };
                    return Task.FromResult(forbiddenResp);
                }

                // Second attempt with refreshed token succeeds
                request.Headers.Authorization!.Parameter.Should().Be("refreshed_user_token");
                var successResp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        data = new { id = "12345678" }
                    }))
                };
                return Task.FromResult(successResp);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var httpClient = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("WykopClient").Returns(httpClient);
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var logger = Substitute.For<ILogger<WykopClient>>();
        var client = new WykopClient(httpClientFactory, memoryCache, options, logger);

        // Act
        var entryId = await client.CreateEntryAsync("Treść wpisu testowego");

        // Assert
        entryId.Should().Be("12345678");
        entriesCallCount.Should().Be(2);
        refreshCalled.Should().BeTrue();
    }
}
