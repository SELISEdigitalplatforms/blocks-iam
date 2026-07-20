using Authentication.DomainService.Authentication;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Mfa.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth
{
    public class MfaAuditServiceTests : IDisposable
    {
        private readonly Mock<IAuthenticationDomainService> _authDomain = new();
        private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();
        private readonly Mock<IUserActivityDispatcher> _dispatcher = new();

        public MfaAuditServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "ctx-tenant", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "ctx-tenant", impersonationSessionId: null, applicationDomain: "test"));

            _authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>()))
                .Returns(new DeviceInformation { Browser = "Chrome", OS = "macOS" });
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private MfaAuditService Create() =>
            new(_authDomain.Object, _httpContextAccessor.Object, _dispatcher.Object, NullLogger<MfaAuditService>.Instance);

        [Fact]
        public async Task Write_MapsAuditEventFields_AndDispatches()
        {
            UserActivityEvent? captured = null;
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var evt = new MfaAuditEvent
            {
                EventType = "mfa.verify",
                UserId = "user-9",
                ClientId = "client-1",
                TenantId = "explicit-tenant",
                IpAddress = "9.9.9.9",
                UserAgent = "UA/1.0",
                Status = IdpConstants.StatusSuccess,
                Details = "ok",
                MfaType = UserMfaType.TOTP
            };

            await Create().WriteAsync(evt);

            captured.Should().NotBeNull();
            captured!.UserId.Should().Be("user-9");
            captured.ClientId.Should().Be("client-1");
            captured.TenantId.Should().Be("explicit-tenant");
            captured.Category.Should().Be(UserActivityCategory.Audit);
            captured.Event.Should().Be("mfa.verify");
            captured.Source.Should().Be("auth-mfa");
            captured.Severity.Should().Be("low");
            captured.Outcome.Should().Be(IdpConstants.StatusSuccess);
            captured.ReasonCode.Should().Be("ok");
            captured.Context.IpAddress.Should().Be("9.9.9.9");
            captured.Context.UserAgent.Should().Be("UA/1.0");
            captured.Context.DeviceInformation!.Browser.Should().Be("Chrome");
            captured.Metadata.Should().ContainKey("mfaType").WhoseValue.Should().Be(UserMfaType.TOTP.ToString());
        }

        [Fact]
        public async Task Write_FailureStatus_UsesHighSeverity()
        {
            UserActivityEvent? captured = null;
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var evt = new MfaAuditEvent { EventType = "mfa.verify", UserId = "u", Status = IdpConstants.StatusFailure };
            await Create().WriteAsync(evt);

            captured!.Severity.Should().Be("high");
            captured.Outcome.Should().Be(IdpConstants.StatusFailure);
        }

        [Fact]
        public async Task Write_NullTenantId_FallsBackToBlocksContextTenant()
        {
            UserActivityEvent? captured = null;
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var evt = new MfaAuditEvent { EventType = "mfa", UserId = "u", TenantId = null };
            await Create().WriteAsync(evt);

            captured!.TenantId.Should().Be("ctx-tenant");
        }

        [Fact]
        public async Task Write_NullUserId_UsesEmptyString()
        {
            UserActivityEvent? captured = null;
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var evt = new MfaAuditEvent { EventType = "mfa", UserId = null };
            await Create().WriteAsync(evt);

            captured!.UserId.Should().Be(string.Empty);
        }

        [Fact]
        public async Task Write_NoMfaType_MetadataIsNull()
        {
            UserActivityEvent? captured = null;
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var evt = new MfaAuditEvent { EventType = "mfa", UserId = "u", MfaType = null };
            await Create().WriteAsync(evt);

            captured!.Metadata.Should().BeNull();
        }

        [Fact]
        public async Task Write_ResolvesIpAndUserAgentFromHttpContext_WhenNotProvided()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers.UserAgent = "HeaderUA/2.0";
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.5");
            _httpContextAccessor.Setup(a => a.HttpContext).Returns(ctx);

            UserActivityEvent? captured = null;
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var evt = new MfaAuditEvent { EventType = "mfa", UserId = "u", IpAddress = null, UserAgent = null };
            await Create().WriteAsync(evt);

            captured!.Context.UserAgent.Should().Be("HeaderUA/2.0");
            captured.Context.IpAddress.Should().Be("10.0.0.5");
        }

        [Fact]
        public async Task Write_NoHttpContext_IpIsUnknown()
        {
            _httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext)null!);

            UserActivityEvent? captured = null;
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .Callback<UserActivityEvent>(e => captured = e)
                .Returns(Task.CompletedTask);

            var evt = new MfaAuditEvent { EventType = "mfa", UserId = "u", IpAddress = null, UserAgent = null };
            await Create().WriteAsync(evt);

            captured!.Context.IpAddress.Should().Be("unknown");
            captured.Context.UserAgent.Should().Be(string.Empty);
        }

        [Fact]
        public async Task Write_DispatcherThrows_IsSwallowed()
        {
            _dispatcher.Setup(d => d.SendUserActivityAsync(It.IsAny<UserActivityEvent>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var evt = new MfaAuditEvent { EventType = "mfa", UserId = "u" };

            // Should not throw despite dispatcher failure.
            await Create().Invoking(s => s.WriteAsync(evt)).Should().NotThrowAsync();
        }
    }
}
