using System.Net;
using SyntInfo.Infrastructure.Services;
using SyntInfo.UnitTests.TestHelpers;

namespace SyntInfo.UnitTests.Services;

public class RemoteImageDownloaderTests
{
    [Theory]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://localhost:8080/secret.png")]
    [InlineData("http://127.0.0.1/admin")]
    public async Task DownloadImageAsync_WhenLocalFileOrLoopbackUrl_ReturnsNullAndDoesNotSendHttpRequest(string unsafeInput)
    {
        // Arrange
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var logger = Substitute.For<ILogger>();

        // Act
        var result = await RemoteImageDownloader.DownloadImageAsync(
            httpClientFactory, unsafeInput, "default.jpg", logger, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        httpClientFactory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task DownloadImageAsync_WhenValidPublicHttpsUrl_DownloadsBytesAndExtractsFileName()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 10, 20, 30 })
        }));
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        var logger = Substitute.For<ILogger>();

        // Act
        var result = await RemoteImageDownloader.DownloadImageAsync(
            httpClientFactory, "https://cdn.example.com/images/banner.webp?v=1", "default.jpg", logger, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Value.FileName.Should().Be("banner.webp");
        result.Value.Bytes.Should().Equal(10, 20, 30);
        RemoteImageDownloader.GetMediaType(result.Value.FileName).Should().Be("image/webp");
    }
}

