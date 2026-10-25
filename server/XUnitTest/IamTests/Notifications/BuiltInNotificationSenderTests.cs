using System.Net;
using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.IamTests.Notifications
{
    /// <summary>Captures log lines so tests can check what is (not) written.</summary>
    internal sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add($"{logLevel}: {formatter(state, exception)} {exception}");
        }
    }

    /// <summary>Answers every request with a function and records what it received.</summary>
    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return await _respond(request, cancellationToken);
        }

        public static StubHandler Status(HttpStatusCode code) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(code)));
    }

    public class BuiltInNotificationSenderTests
    {
        private const string Url = "https://logic.test/api/Notifier/SendSecretNotification";
        private const string TenantId = "tenant-1";
        private const string Salt = "salt-value";
        private const string Hash = "signed-hash";

        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICryptoService> _crypto = new();
        private readonly ListLogger<BuiltInNotificationSender> _logger = new();

        public BuiltInNotificationSenderTests()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(new Tenant
            {
                TenantId = TenantId,
                TenantSalt = Salt,
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
            });
            _crypto.Setup(c => c.Hash(TenantId, Salt)).Returns(Hash);
        }

        private BuiltInNotificationSender Create(StubHandler handler, string? url = Url)
        {
            var settings = new Dictionary<string, string?>();
            if (url != null)
            {
                settings[BuiltInNotificationSender.UrlConfigKey] = url;
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
            return new BuiltInNotificationSender(configuration, factory.Object, _tenants.Object, _crypto.Object, _logger);
        }

        private static BuiltInNotification Notification(params string[] userIds) =>
            new(TenantId, BuiltInNotificationNames.UserDeactivated, userIds,
                BuiltInNotificationSender.Payload(BuiltInNotificationNames.UserDeactivated, "t", "d",
                    fields: new Dictionary<string, object?> { ["userId"] = "u1" }));

        [Fact]
        public async Task Send_PostsToNotifier_WithTenantHeaders_AndContract()
        {
            var handler = StubHandler.Status(HttpStatusCode.OK);

            var ok = await Create(handler).SendAsync(Notification("u1", "u1", ""));

            ok.Should().BeTrue();
            handler.Requests.Should().ContainSingle();
            var request = handler.Requests[0];
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.ToString().Should().Be(Url);
            request.Headers.GetValues("x-blocks-key").Should().Equal(TenantId);
            request.Headers.GetValues("Secret").Should().Equal(Hash);

            using var body = JsonDocument.Parse(handler.Bodies[0]);
            var root = body.RootElement;
            root.GetProperty("ConfigurationName").GetString().Should().Be("iam-user-deactivated");
            root.GetProperty("UserIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("u1");
            root.GetProperty("Roles").GetArrayLength().Should().Be(0);
            root.GetProperty("ContentAvailable").GetBoolean().Should().BeTrue();
            root.GetProperty("SaveDenormalizedPayloadAsAnObject").GetBoolean().Should().BeFalse();

            using var payload = JsonDocument.Parse(root.GetProperty("DenormalizedPayload").GetString()!);
            payload.RootElement.GetProperty("event").GetString().Should().Be("iam-user-deactivated");
            payload.RootElement.GetProperty("userId").GetString().Should().Be("u1");
            payload.RootElement.TryGetProperty("occurredAt", out _).Should().BeTrue();
        }

        [Fact]
        public async Task Send_NoRecipient_MakesNoCall()
        {
            var handler = StubHandler.Status(HttpStatusCode.OK);

            var ok = await Create(handler).SendAsync(Notification(" ", ""));

            ok.Should().BeFalse();
            handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Send_UrlNotConfigured_MakesNoCall()
        {
            var handler = StubHandler.Status(HttpStatusCode.OK);

            var ok = await Create(handler, url: null).SendAsync(Notification("u1"));

            ok.Should().BeFalse();
            handler.Requests.Should().BeEmpty();
            _logger.Lines.Should().Contain(l => l.Contains("NotificationServiceUrl is not configured"));
        }

        [Fact]
        public async Task Send_UnknownTenant_MakesNoCall()
        {
            var handler = StubHandler.Status(HttpStatusCode.OK);
            var notification = Notification("u1") with { TenantId = "other-tenant" };

            var ok = await Create(handler).SendAsync(notification);

            ok.Should().BeFalse();
            handler.Requests.Should().BeEmpty();
        }

        [Fact]
        public async Task Send_NotifierError_ReturnsFalse_AndLogsStatusOnly()
        {
            var handler = StubHandler.Status(HttpStatusCode.InternalServerError);

            var ok = await Create(handler).SendAsync(Notification("u1"));

            ok.Should().BeFalse();
            _logger.Lines.Should().Contain(l => l.StartsWith("Warning") && l.Contains("status=500") && l.Contains("iam-user-deactivated") && l.Contains(TenantId));
        }

        [Fact]
        public async Task Send_HandlerThrows_IsSwallowed_AndMessageNotLogged()
        {
            var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused to secret-host"));

            var act = async () => await Create(handler).SendAsync(Notification("u1"));

            (await act.Should().NotThrowAsync()).Subject.Should().BeFalse();
            _logger.Lines.Should().Contain(l => l.Contains("error=HttpRequestException"));
            _logger.Lines.Should().NotContain(l => l.Contains("secret-host"));
        }

        [Fact]
        public async Task Send_SlowNotifier_IsCancelledByTimeout()
        {
            var handler = new StubHandler(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var ok = await Create(handler).SendAsync(Notification("u1"));
            watch.Stop();

            ok.Should().BeFalse();
            BuiltInNotificationSender.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
        }

        [Fact]
        public async Task Send_NeverLogsSecretOrPayload()
        {
            var handler = StubHandler.Status(HttpStatusCode.OK);

            await Create(handler).SendAsync(Notification("u1"));

            _logger.Lines.Should().NotContain(l => l.Contains(Hash) || l.Contains(Salt) || l.Contains("userId"));
        }

        [Fact]
        public async Task TrySend_NullSender_OrThrowingSender_DoesNotThrow()
        {
            IBuiltInNotificationSender? none = null;
            await none.TrySendAsync(Notification("u1"));

            var throwing = new Mock<IBuiltInNotificationSender>();
            throwing.Setup(s => s.SendAsync(It.IsAny<BuiltInNotification>())).ThrowsAsync(new InvalidOperationException());
            var act = async () => await throwing.Object.TrySendAsync(Notification("u1"));
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void Payload_SkipsNullFields_AndKeepsCommonShape()
        {
            var payload = BuiltInNotificationSender.Payload("cfg", "title", "desc", true,
                new Dictionary<string, object?> { ["a"] = "1", ["b"] = null });

            payload.Keys.Should().BeEquivalentTo("event", "title", "description", "isSuccess", "a", "occurredAt");
        }
    }
}
