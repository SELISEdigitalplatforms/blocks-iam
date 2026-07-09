using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Oidc
{
    public class IdpSessionServiceTests
    {
        private static IdpSessionService CreateService(
            out Mock<IIdpSessionRepository> sessionRepo,
            out Mock<IAuditLogRepository> auditRepo,
            out Mock<IAuthenticationRepository> authRepo,
            out Mock<IRefreshTokenRepository> refreshRepo)
        {
            sessionRepo = new Mock<IIdpSessionRepository>();
            auditRepo = new Mock<IAuditLogRepository>();
            authRepo = new Mock<IAuthenticationRepository>();
            refreshRepo = new Mock<IRefreshTokenRepository>();

            return new IdpSessionService(
                sessionRepo.Object,
                auditRepo.Object,
                authRepo.Object,
                refreshRepo.Object,
                Mock.Of<ICacheClient>(),
                NullLogger<IdpSessionService>.Instance);
        }

        [Fact]
        public async Task CreateSessionAsync_PersistsSession_AndReturnsId()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            sessionRepo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>())).ReturnsAsync("id");

            var sessionId = await service.CreateSessionAsync("user-1", "tenant-1", "127.0.0.1");

            sessionId.Should().NotBeNullOrEmpty();
            sessionRepo.Verify(r => r.CreateAsync(It.Is<IdpSessionModel>(m =>
                m.TenantId == "tenant-1" &&
                m.IpAddress == "127.0.0.1" &&
                m.Accounts.Count == 1
            )), Times.Once);
        }

        [Fact]
        public async Task CreateSessionAsync_LogsAuditEvent_OnSuccess()
        {
            var service = CreateService(out var sessionRepo, out var auditRepo, out _, out _);
            sessionRepo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>())).ReturnsAsync("id");

            await service.CreateSessionAsync("user-1", "tenant-1", "127.0.0.1");

            auditRepo.Verify(a => a.CreateAsync(It.Is<AuditLogModel>(m =>
                m.EventType == Authentication.DomainService.Authentication.SessionAuditEvents.SessionCreated &&
                m.UserId == "user-1"
            )), Times.Once);
        }

        [Fact]
        public async Task GetSessionAsync_DelegatesToRepository()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var expected = new IdpSessionModel { SessionId = "s1" };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(expected);

            var session = await service.GetSessionAsync("s1");

            session.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task AddAccountAsync_ReturnsFalse_WhenSessionNotFound()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync((IdpSessionModel?)null);

            var result = await service.AddAccountAsync("s1", "user-1", "tenant-1", "User 1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task AddAccountAsync_ReturnsTrue_WhenAccountAlreadyExists()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var session = new IdpSessionModel
            {
                SessionId = "s1",
                Accounts =
                [
                    new IdpSessionAccount { UserId = "user-1", TenantId = "tenant-1" }
                ]
            };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(session);

            var result = await service.AddAccountAsync("s1", "user-1", "tenant-1", "User 1");

            result.Should().BeTrue();
            sessionRepo.Verify(r => r.AddAccountAsync(It.IsAny<string>(), It.IsAny<IdpSessionAccount>()), Times.Never);
        }

        [Fact]
        public async Task AddAccountAsync_AddsAccount_WhenNew()
        {
            var service = CreateService(out var sessionRepo, out var auditRepo, out _, out _);
            var session = new IdpSessionModel { SessionId = "s1", Accounts = [] };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(session);
            sessionRepo.Setup(r => r.AddAccountAsync("s1", It.IsAny<IdpSessionAccount>())).ReturnsAsync(true);

            var result = await service.AddAccountAsync("s1", "user-1", "tenant-1", "User 1");

            result.Should().BeTrue();
            auditRepo.Verify(a => a.CreateAsync(It.Is<AuditLogModel>(m =>
                m.EventType == Authentication.DomainService.Authentication.SessionAuditEvents.AccountAdded
            )), Times.Once);
        }

        [Fact]
        public async Task SelectAccountAsync_ReturnsFalse_WhenSessionNotFound()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync((IdpSessionModel?)null);

            var result = await service.SelectAccountAsync("s1", "user-1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task SelectAccountAsync_ReturnsFalse_WhenAccountNotInSession()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var session = new IdpSessionModel { SessionId = "s1", Accounts = [] };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(session);

            var result = await service.SelectAccountAsync("s1", "user-1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task SelectAccountAsync_UpdatesActivity_WhenAccountExists()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var session = new IdpSessionModel
            {
                SessionId = "s1",
                Accounts = [new IdpSessionAccount { UserId = "user-1" }]
            };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(session);
            sessionRepo.Setup(r => r.UpdateActivityAsync("s1")).ReturnsAsync(true);

            var result = await service.SelectAccountAsync("s1", "user-1");

            result.Should().BeTrue();
            sessionRepo.Verify(r => r.UpdateActivityAsync("s1"), Times.Once);
        }

        [Fact]
        public async Task RemoveAccountAsync_DeletesSession_WhenOnlyOneAccount()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var session = new IdpSessionModel
            {
                SessionId = "s1",
                Accounts = [new IdpSessionAccount { UserId = "user-1", TenantId = "tenant-1" }]
            };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(session);
            sessionRepo.Setup(r => r.DeleteAsync("s1")).ReturnsAsync(true);

            var result = await service.RemoveAccountAsync("s1", "user-1", "tenant-1");

            result.Should().BeTrue();
            sessionRepo.Verify(r => r.DeleteAsync("s1"), Times.Once);
        }

        [Fact]
        public async Task RemoveAccountAsync_RemovesFromMulti_WhenMultipleAccounts()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var session = new IdpSessionModel
            {
                SessionId = "s1",
                Accounts =
                [
                    new IdpSessionAccount { UserId = "user-1", TenantId = "tenant-1" },
                    new IdpSessionAccount { UserId = "user-2", TenantId = "tenant-1" }
                ]
            };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(session);
            sessionRepo.Setup(r => r.RemoveAccountAsync("s1", "user-1", "tenant-1")).ReturnsAsync(true);

            var result = await service.RemoveAccountAsync("s1", "user-1", "tenant-1");

            result.Should().BeTrue();
            sessionRepo.Verify(r => r.RemoveAccountAsync("s1", "user-1", "tenant-1"), Times.Once);
            sessionRepo.Verify(r => r.DeleteAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RemoveAccountAsync_ReturnsFalse_WhenAccountNotFound()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var session = new IdpSessionModel { SessionId = "s1", Accounts = [] };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(session);

            var result = await service.RemoveAccountAsync("s1", "user-1", "tenant-1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateActivityAsync_ReturnsFalse_WhenSessionExpired()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var session = new IdpSessionModel
            {
                SessionId = "s1",
                IdleExpiry = DateTime.UtcNow.AddHours(-1),
                AbsoluteExpiry = DateTime.UtcNow.AddHours(-1)
            };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(session);

            var result = await service.UpdateActivityAsync("s1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsSessionActiveAsync_ReturnsFalse_WhenSessionNull()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync((IdpSessionModel?)null);

            var result = await service.IsSessionActiveAsync("s1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsSessionActiveAsync_ReturnsTrue_WhenSessionActive()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var session = new IdpSessionModel
            {
                SessionId = "s1",
                IdleExpiry = DateTime.UtcNow.AddHours(1),
                AbsoluteExpiry = DateTime.UtcNow.AddHours(5)
            };
            sessionRepo.Setup(r => r.GetBySessionIdAsync("s1")).ReturnsAsync(session);

            var result = await service.IsSessionActiveAsync("s1");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task GetUserSessionsAsync_DelegatesToRepository()
        {
            var service = CreateService(out var sessionRepo, out _, out _, out _);
            var expected = new List<IdpSessionModel> { new() { SessionId = "s1" } };
            sessionRepo.Setup(r => r.GetByUserAsync("user-1", "tenant-1")).ReturnsAsync(expected);

            var result = await service.GetUserSessionsAsync("user-1", "tenant-1");

            result.Should().BeEquivalentTo(expected);
        }
    }
}