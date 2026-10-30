using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SyntInfo.Application.Options;
using SyntInfo.Infrastructure.Services;
using SyntInfo.UnitTests.TestHelpers;

namespace SyntInfo.UnitTests.Services;

public class MailerLiteClientTests
{
    private readonly ILogger<MailerLiteClient> _logger = Substitute.For<ILogger<MailerLiteClient>>();

    [Fact]
    public async Task SendNewsletterCampaignAsync_WhenUseMockTrue_ReturnsMockIdWithoutLiteralSliceSyntax()
    {
        // Arrange
        var factory = Substitute.For<IHttpClientFactory>();
        var options = Options.Create(new MailerLiteOptions { UseMock = true });
        var client = new MailerLiteClient(factory, options, _logger);

        // Act
        var campaignId = await client.SendNewsletterCampaignAsync("Temat", "<p>HTML</p>", "Tekst");

        // Assert
        campaignId.Should().StartWith("mailerlite-mock-");
        campaignId.Should().NotContain("[..8]");
        campaignId.Should().HaveLength(24);
    }

    [Fact]
    public async Task SendNewsletterCampaignAsync_WhenUseMockFalse_CreatesAndSchedulesCampaign()
    {
        // Arrange
        var options = Options.Create(new MailerLiteOptions
        {
            UseMock = false,
            ApiKey = "ml_live_api_key",
            BaseUrl = "https://connect.mailerlite.com/api/",
            FromEmail = "newsletter@infoskrot.pl",
            FromName = "InfoSkrót"
        });

        bool createCalled = false;
        bool scheduleCalled = false;

        var handler = new TestHttpMessageHandler(request =>
        {
            var uri = request.RequestUri!.ToString();
            if (uri.EndsWith("/campaigns"))
            {
                createCalled = true;
                var json = JsonSerializer.Serialize(new { data = new { id = "camp-987" } });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(json)
                });
            }

            if (uri.EndsWith("/campaigns/camp-987/schedule"))
            {
                scheduleCalled = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("MailerLiteClient").Returns(new HttpClient(handler));

        var client = new MailerLiteClient(factory, options, _logger);

        // Act
        var result = await client.SendNewsletterCampaignAsync("Wydanie dnia", "<html></html>", "Tekst");

        // Assert
        result.Should().Be("camp-987");
        createCalled.Should().BeTrue();
        scheduleCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeAsync_And_UnsubscribeAsync_SendExpectedStatusToSubscribersEndpoint()
    {
        // Arrange
        var options = Options.Create(new MailerLiteOptions
        {
            UseMock = false,
            ApiKey = "ml_live_api_key",
            BaseUrl = "https://connect.mailerlite.com/api/"
        });

        var payloads = new List<string>();
        var handler = new TestHttpMessageHandler(async request =>
        {
            if (request.RequestUri!.ToString().EndsWith("/subscribers"))
            {
                payloads.Add(await request.Content!.ReadAsStringAsync());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("MailerLiteClient").Returns(new HttpClient(handler));
        var client = new MailerLiteClient(factory, options, _logger);

        // Act
        var subOk = await client.SubscribeAsync("User@Example.com", "Jan");
        var unsubOk = await client.UnsubscribeAsync("User@Example.com");

        // Assert
        subOk.Should().BeTrue();
        unsubOk.Should().BeTrue();
        payloads.Should().HaveCount(2);
        payloads[0].Should().Contain("\"email\":\"user@example.com\"").And.Contain("\"status\":\"active\"");
        payloads[1].Should().Contain("\"email\":\"user@example.com\"").And.Contain("\"status\":\"unsubscribed\"");
    }
}

