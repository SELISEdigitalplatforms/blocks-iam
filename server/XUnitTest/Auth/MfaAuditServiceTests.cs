using Authentication.DomainService.Authentication;
using Iam.DomainService.Utilities;
using global::Authentication.DomainService.Oidc.Repositories;
using global::Authentication.DomainService.Shared;
using Blocks.Genesis;
using global::Idp.DomainService.Oidc.Contracts;
using global::Iam.DomainService.Entities;
using global::Mfa.DomainService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth
{
    public class MfaAuditServiceTests
    {
        private static MfaAuditService Create(
            out Mock<IAuditLogRepository> repo,
            out DefaultHttpContext httpContext)
        {
            repo = new Mock<IAuditLogRepository>();
            httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.SetupGet(a => a.HttpContext).Returns(httpContext);
            return new MfaAuditService(repo.Object, accessor.Object, NullLogger<MfaAuditService>.Instance);
        }

        [Fact]
        public async Task WriteAsync_PersistsAuditLog()
        {
            var service = Create(out var repo, out _);
            var evt = new MfaAuditEvent
            {
                EventType = "mfa_test",
                UserId = "user-1",
                ClientId = "client-1",
                TenantId = "tenant-1",
                Status = IdpConstants.StatusSuccess,
                Severity = IdpConstants.SeverityInfo,
                Details = "test"
            };

            await service.WriteAsync(evt);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m =>
                m.EventType == "mfa_test" &&
                m.UserId == "user-1" &&
                m.ClientId == "client-1" &&
                m.TenantId == "tenant-1" &&
                m.Severity == IdpConstants.SeverityInfo &&
                m.Status == IdpConstants.StatusSuccess
            )), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_SetsTimestampToUtcNow()
        {
            var service = Create(out var repo, out _);
            var before = DateTime.UtcNow.AddSeconds(-1);
            var evt = new MfaAuditEvent { EventType = "evt", UserId = "u" };

            await service.WriteAsync(evt);

            var after = DateTime.UtcNow.AddSeconds(1);
            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m =>
                m.Timestamp >= before && m.Timestamp <= after)), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_UsesExplicitIp_WhenProvided()
        {
            var service = Create(out var repo, out _);
            var evt = new MfaAuditEvent { EventType = "evt", UserId = "u", IpAddress = "10.0.0.1" };

            await service.WriteAsync(evt);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.IpAddress == "10.0.0.1")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_FallsBackToContextIpAddress()
        {
            var service = Create(out var repo, out _);
            var evt = new MfaAuditEvent { EventType = "evt", UserId = "u" };

            await service.WriteAsync(evt);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.IpAddress == "127.0.0.1")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_FallsBackToUnknown_WhenContextHasNoIp()
        {
            var repo = new Mock<IAuditLogRepository>();
            var httpContext = new DefaultHttpContext();
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.SetupGet(a => a.HttpContext).Returns(httpContext);
            var service = new MfaAuditService(repo.Object, accessor.Object, NullLogger<MfaAuditService>.Instance);

            await service.WriteAsync(new MfaAuditEvent { EventType = "evt", UserId = "u" });

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.IpAddress == "unknown")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_UsesExplicitUserAgent_WhenProvided()
        {
            var service = Create(out var repo, out _);
            var evt = new MfaAuditEvent { EventType = "evt", UserId = "u", UserAgent = "Custom/1.0" };

            await service.WriteAsync(evt);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.UserAgent == "Custom/1.0")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_FallsBackToContextUserAgent()
        {
            var service = Create(out var repo, out var httpContext);
            httpContext.Request.Headers.UserAgent = "Browser/1.0";
            var evt = new MfaAuditEvent { EventType = "evt", UserId = "u" };

            await service.WriteAsync(evt);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.UserAgent == "Browser/1.0")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_FallsBackToEventType_WhenDetailsMissing()
        {
            var service = Create(out var repo, out _);
            var evt = new MfaAuditEvent { EventType = "mfa_test", UserId = "u" };

            await service.WriteAsync(evt);

            repo.Verify(r => r.CreateAsync(It.Is<AuditLogModel>(m => m.Details == "mfa_test")), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_SwallowsRepositoryException()
        {
            var repo = new Mock<IAuditLogRepository>();
            repo.Setup(r => r.CreateAsync(It.IsAny<AuditLogModel>())).ThrowsAsync(new Exception("db error"));
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext());

            var service = new MfaAuditService(repo.Object, accessor.Object, NullLogger<MfaAuditService>.Instance);

            var act = async () => await service.WriteAsync(new MfaAuditEvent { EventType = "evt", UserId = "u" });
            await act.Should().NotThrowAsync();
        }
    }
}