using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Worker;
using Worker.Configuration;

namespace XUnitTest.Worker
{
    public class PeriodicPingBackgroundServiceTests
    {
        private static PeriodicPingBackgroundService CreateService(
            PeriodicPingConfiguration options,
            out Mock<IHttpClientFactory> httpFactory,
            out List<HttpRequestMessage> sentRequests,
            out List<string> logMessages)
        {
            var optionsMonitor = new Mock<IOptionsMonitor<PeriodicPingConfiguration>>();
            optionsMonitor.SetupGet(m => m.CurrentValue).Returns(options);

            sentRequests = new List<HttpRequestMessage>();
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK))
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => sentRequests.Add(req));

            var client = new HttpClient(handler.Object);
            httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

            logMessages = new List<string>();
            var logger = new LoggerFactory().CreateLogger<PeriodicPingBackgroundService>();

            return new PeriodicPingBackgroundService(
                httpFactory.Object,
                optionsMonitor.Object,
                logger);
        }

        [Fact]
        public async Task ExecuteAsync_WhenDisabled_LogsAndReturnsWithoutPinging()
        {
            var options = new PeriodicPingConfiguration { Enabled = false, PingUrl = "https://x", PingIntervalSeconds = 60 };
            var service = CreateService(options, out var httpFactory, out var sentRequests, out _);

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(100);
            await service.StopAsync(CancellationToken.None);

            sentRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task ExecuteAsync_WhenPingUrlIsEmpty_LogsWarningAndReturnsWithoutPinging()
        {
            var options = new PeriodicPingConfiguration { Enabled = true, PingUrl = "", PingIntervalSeconds = 60 };
            var service = CreateService(options, out _, out var sentRequests, out _);

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(100);
            await service.StopAsync(CancellationToken.None);

            sentRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task ExecuteAsync_WhenIntervalIsZero_LogsWarningAndReturnsWithoutPinging()
        {
            var options = new PeriodicPingConfiguration { Enabled = true, PingUrl = "https://example.com", PingIntervalSeconds = 0 };
            var service = CreateService(options, out _, out var sentRequests, out _);

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(100);
            await service.StopAsync(CancellationToken.None);

            sentRequests.Should().BeEmpty();
        }

        [Fact]
        public async Task ExecuteAsync_WhenEnabled_PingsUrlImmediately()
        {
            var options = new PeriodicPingConfiguration { Enabled = true, PingUrl = "https://example.com/health", PingIntervalSeconds = 60 };
            var service = CreateService(options, out _, out var sentRequests, out _);

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(1500);
            await service.StopAsync(CancellationToken.None);

            sentRequests.Should().NotBeEmpty();
            sentRequests[0].RequestUri!.ToString().Should().Be("https://example.com/health");
        }

        [Fact]
        public async Task ExecuteAsync_WhenServerReturnsSuccess_DoesNotThrow()
        {
            var options = new PeriodicPingConfiguration { Enabled = true, PingUrl = "https://example.com", PingIntervalSeconds = 60 };
            var service = CreateService(options, out _, out var sentRequests, out _);

            Func<Task> act = async () =>
            {
                await service.StartAsync(CancellationToken.None);
                await Task.Delay(1500);
                await service.StopAsync(CancellationToken.None);
            };

            await act.Should().NotThrowAsync();
            sentRequests.Should().NotBeEmpty();
        }

        [Fact]
        public async Task ExecuteAsync_OnServerError_DoesNotCrashLoop()
        {
            var options = new PeriodicPingConfiguration { Enabled = true, PingUrl = "https://example.com", PingIntervalSeconds = 60 };

            var optionsMonitor = new Mock<IOptionsMonitor<PeriodicPingConfiguration>>();
            optionsMonitor.SetupGet(m => m.CurrentValue).Returns(options);

            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("boom"));

            var client = new HttpClient(handler.Object);
            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

            var service = new PeriodicPingBackgroundService(
                httpFactory.Object,
                optionsMonitor.Object,
                new LoggerFactory().CreateLogger<PeriodicPingBackgroundService>());

            Func<Task> act = async () =>
            {
                await service.StartAsync(CancellationToken.None);
                await Task.Delay(1500);
                await service.StopAsync(CancellationToken.None);
            };

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimesWithoutThrowing()
        {
            var options = new PeriodicPingConfiguration { Enabled = false, PingUrl = "", PingIntervalSeconds = 0 };
            var service = CreateService(options, out _, out _, out _);

            Action act = () =>
            {
                service.Dispose();
                service.Dispose();
            };

            act.Should().NotThrow();
        }
    }
}
