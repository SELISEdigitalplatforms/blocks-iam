using Authentication.DomainService.Authentication;
using Iam.DomainService.Utilities;
using global::Authentication.DomainService.Entities;
using global::Authentication.DomainService.OAuth;
using global::Authentication.DomainService.Oidc.Repositories;
using global::Authentication.DomainService.Services;
using global::Authentication.DomainService.Shared;
using Blocks.Genesis;
using global::Idp.DomainService.Oidc.Contracts;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth
{
    public class OidcLoginAuditWriterTests
    {
        private static OidcLoginAuditWriter Create(out Mock<IAuditLogRepository> repo, out Mock<IAuthenticationDomainService> authDomain)
        {
            repo = new Mock<IAuditLogRepository>();
            authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.SendToQueueAsync(It.IsAny<string>(), It.IsAny<It.IsAnyType>()))
                .Returns(Task.CompletedTask);
            return new OidcLoginAuditWriter(repo.Object, authDomain.Object, NullLogger<OidcLoginAuditWriter>.Instance);
        }

        private static OidcLoginRequest BuildRequest() => new()
        {
            ClientId = "client-1",
            TenantId = "tenant-1"
        };

        private static User BuildUser() => new() { ItemId = "user-1" };

        private static HttpRequest BuildRequestWithIp(string ip = "127.0.0.1")
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
            ctx.Request.Headers.UserAgent = "Browser/1.0";
            return ctx.Request;
        }

        [Fact]
        public async Task WriteAsync_PersistsAuditLog()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "login_success", "details");

            repo.Verify(r => r.CreateAsync(It.IsAny<AuditLogModel>()), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_SetsEventType()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "login_success", "details");

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.EventType == "login_success")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_MarksAsInfoAndSuccess_OnSuccess()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "login_success", "details");

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m =>
                m.Severity == IdpConstants.SeverityInfo &&
                m.Status == IdpConstants.StatusSuccess
            )), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_MarksAsWarnAndFailure_OnFailure()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "login_failure", "details");

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m =>
                m.Severity == IdpConstants.SeverityWarn &&
                m.Status == IdpConstants.StatusFailure
            )), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_MarksAsWarn_OnLocked()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "login_locked", "details");

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m =>
                m.Severity == IdpConstants.SeverityWarn
            )), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_SetsUserAndClientAndTenant()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "login_success", null);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m =>
                m.UserId == "user-1" &&
                m.ClientId == "client-1" &&
                m.TenantId == "tenant-1"
            )), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_UsesProvidedDetails_WhenSupplied()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "login_success", "my-details");

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.Details == "my-details")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_FallsBackToEventType_WhenDetailsNull()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "login_success", null);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.Details == "login_success")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_SwallowsRepositoryException()
        {
            var repo = new Mock<IAuditLogRepository>();
            repo.Setup(r => r.CreateAsync(It.IsAny<AuditLogModel>())).ThrowsAsync(new Exception("db error"));
            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.SendToQueueAsync(It.IsAny<string>(), It.IsAny<It.IsAnyType>()))
                .Returns(Task.CompletedTask);
            var writer = new OidcLoginAuditWriter(repo.Object, authDomain.Object, NullLogger<OidcLoginAuditWriter>.Instance);

            var act = async () => await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "evt", null);
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task WriteAsync_CapturesUserAgent()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp(), "login_success", null);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.UserAgent == "Browser/1.0")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_CapturesIpAddress()
        {
            var writer = Create(out var repo, out _);
            await writer.WriteAsync(BuildRequest(), BuildUser(), BuildRequestWithIp("10.0.0.5"), "login_success", null);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.IpAddress == "10.0.0.5")), Times.Once);
        }
    }
}