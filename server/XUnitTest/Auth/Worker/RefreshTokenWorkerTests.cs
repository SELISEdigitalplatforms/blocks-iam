using Authentication.DomainService.Worker;
using global::Authentication.DomainService.Entities;
using global::Authentication.DomainService.Services;
using global::Iam.DomainService.Dtos;
using global::Iam.DomainService.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Worker
{
    public class RefreshTokenWorkerTests
    {
        private static RefreshTokenWorkerService CreateWorker(
            out Mock<IAuthenticationRepository> authRepo,
            out Mock<IUserRepository> userRepo)
        {
            authRepo = new Mock<IAuthenticationRepository>();
            userRepo = new Mock<IUserRepository>();
            return new RefreshTokenWorkerService(
                NullLogger<RefreshTokenWorkerService>.Instance,
                authRepo.Object,
                userRepo.Object);
        }

        [Fact]
        public async Task Consume_OnLogin_InsertsNewIdentitySession()
        {
            var worker = CreateWorker(out var authRepo, out _);
            authRepo.Setup(r => r.InsertIdentitySessionAsync(It.IsAny<IdentitySession>()))
                .ReturnsAsync(true);

            var evt = BuildEvent(isLogin: true);

            await worker.Consume(evt);

            authRepo.Verify(r => r.InsertIdentitySessionAsync(It.IsAny<IdentitySession>()), Times.Once);
            authRepo.Verify(r => r.UpsertIdentitySessionBySessionIdAsync(It.IsAny<IdentitySession>()), Times.Never);
        }

        [Fact]
        public async Task Consume_OnRenewal_UpsertsIdentitySession()
        {
            var worker = CreateWorker(out var authRepo, out _);
            authRepo.Setup(r => r.UpsertIdentitySessionBySessionIdAsync(It.IsAny<IdentitySession>()))
                .ReturnsAsync(true);

            var evt = BuildEvent(isLogin: false, isRevoke: false);

            await worker.Consume(evt);

            authRepo.Verify(r => r.UpsertIdentitySessionBySessionIdAsync(It.IsAny<IdentitySession>()), Times.Once);
            authRepo.Verify(r => r.InsertIdentitySessionAsync(It.IsAny<IdentitySession>()), Times.Never);
        }

        [Fact]
        public async Task Consume_OnRevoke_DoesNotInsertOrUpsert()
        {
            var worker = CreateWorker(out var authRepo, out _);
            authRepo.Setup(r => r.RevokeIdentitySessionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var evt = BuildEvent(isLogin: false, isRevoke: true);

            await worker.Consume(evt);

            authRepo.Verify(r => r.RevokeIdentitySessionAsync(evt.RefreshToken, evt.UserId), Times.Once);
            authRepo.Verify(r => r.InsertIdentitySessionAsync(It.IsAny<IdentitySession>()), Times.Never);
            authRepo.Verify(r => r.UpsertIdentitySessionBySessionIdAsync(It.IsAny<IdentitySession>()), Times.Never);
        }

        [Fact]
        public async Task ProcessSession_OnRenewal_UpsertsBySessionId()
        {
            var worker = CreateWorker(out var authRepo, out _);
            authRepo.Setup(r => r.UpsertIdentitySessionBySessionIdAsync(It.IsAny<IdentitySession>()))
                .ReturnsAsync(true);

            var result = await worker.ProcessSession(BuildEvent(isLogin: false));

            result.Should().BeTrue();
            authRepo.Verify(r => r.UpsertIdentitySessionBySessionIdAsync(It.Is<IdentitySession>(s =>
                s.SessionId == "session-1" &&
                s.UserId == "user-1" &&
                s.TenantId == "tenant-1" &&
                s.RefreshToken == "rt-new"
            )), Times.Once);
        }

        [Fact]
        public async Task ProcessSession_OnLogin_InsertsNewSession()
        {
            var worker = CreateWorker(out var authRepo, out _);
            authRepo.Setup(r => r.InsertIdentitySessionAsync(It.IsAny<IdentitySession>()))
                .ReturnsAsync(true);

            var result = await worker.ProcessSession(BuildEvent(isLogin: true));

            result.Should().BeTrue();
            authRepo.Verify(r => r.InsertIdentitySessionAsync(It.IsAny<IdentitySession>()), Times.Once);
        }

        private static RefreshTokenEvent BuildEvent(bool isLogin = false, bool isRevoke = false) => new()
        {
            RefreshToken = "rt-new",
            TenantId = "tenant-1",
            UserId = "user-1",
            OrganizationId = "org-1",
            ClientId = "client-1",
            SessionId = "session-1",
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(30),
            IpAddresses = "127.0.0.1",
            DeviceInformation = new DeviceInformation { Device = "Test", OS = "TestOS", Browser = "TestBrowser" },
            IsLogin = isLogin,
            IsRevoke = isRevoke,
            GrantType = "password",
            Outcome = "success",
            ReasonCode = "ok",
            RiskLevel = "low",
            CorrelationId = "corr-1"
        };

        [Fact]
        public async Task ProcessUserTimelineEvent_PopulatesAllExtendedFields()
        {
            var worker = CreateWorker(out var authRepo, out _);

            var result = await worker.ProcessUserTimelineEvent(BuildEvent(isLogin: true));

            result.Should().BeTrue();
            authRepo.Verify(r => r.InsertIdentityEventAsync(It.Is<IdentityEvent>(e =>
                e.UserId == "user-1" &&
                e.TenantId == "tenant-1" &&
                e.SessionId == "session-1" &&
                e.ClientId == "client-1" &&
                e.Outcome == "success" &&
                e.ReasonCode == "ok" &&
                e.RiskLevel == "low" &&
                e.CorrelationId == "corr-1" &&
                e.Event == "login_via_password"
            )), Times.Once);
        }
    }
}
