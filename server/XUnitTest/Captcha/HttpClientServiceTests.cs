using System.Net;
using Blocks.CaptchaDriver;
using FluentAssertions;
using Microsoft.Extensions.Http;
using Moq;
using Moq.Protected;

namespace XUnitTest.Captcha
{
    public class HttpClientServiceTests
    {
        private static HttpClientService CreateService(
            out Mock<IHttpClientFactory> factory,
            out List<HttpRequestMessage> sentRequests,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string responseBody = "ok")
        {
            var localRequests = new List<HttpRequestMessage>();
            sentRequests = localRequests;

            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
                {
                    localRequests.Add(req);
                    var response = new HttpResponseMessage(statusCode)
                    {
                        Content = new StringContent(responseBody)
                    };
                    return Task.FromResult(response);
                });

            factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(handler.Object));

            return new HttpClientService(factory.Object);
        }

        [Fact]
        public async Task SendAsync_AddsContentTypeHeader()
        {
            var service = CreateService(out _, out var sent);

            await service.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x"), "application/json");

            sent.Should().HaveCount(1);
            sent[0].Headers.Should().NotBeNull();
            sent[0].Headers.TryGetValues("Content-Type", out _).Should().BeFalse();
        }

        [Fact]
        public async Task SendAsync_ReturnsResponse()
        {
            var service = CreateService(out _, out _, HttpStatusCode.Accepted, "{\"v\":1}");

            var response = await service.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x"), "text/plain");

            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
            (await response.Content.ReadAsStringAsync()).Should().Be("{\"v\":1}");
        }

        [Fact]
        public async Task SendAsync_UsesNamedClient()
        {
            string? capturedName = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Callback<string>(n => capturedName = n)
                .Returns(() => new HttpClient(handler.Object));

            var service = new HttpClientService(factory.Object);
            await service.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x"), "text/plain");

            capturedName.Should().Be("CaptchaDriver");
        }

        [Fact]
        public async Task SendAsync_ThrowsOnNullRequest()
        {
            var service = CreateService(out _, out _);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(
                () => service.SendAsync(null!, "text/plain"));
        }

        [Fact]
        public async Task SendAsync_ThrowsOnNullContentType()
        {
            var service = CreateService(out _, out _);
            await Assert.ThrowsAnyAsync<ArgumentNullException>(
                () => service.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x"), null!));
        }
    }
}
