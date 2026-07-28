using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Oidc.IdpSessionService
{
    /// <summary>
    /// Covers the public API of <see cref="Authentication.DomainService.Oidc.Services.IdpSessionService"/>
    /// that the sibling <c>IdpSessionServiceTests</c> (ResolveOrCreate only) does not exercise:
    /// create/get/add-account/select/remove/update-activity/rotate/revoke/is-active/get-user-sessions.
    /// </summary>
    public class IdpSessionServiceExtraTests
    {
        private readonly Mock<IIdpSessionRepository> _sessionRepo = new();
        private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();

        private Authentication.DomainService.Oidc.Services.IdpSessionService Create() =>
            new(
                _sessionRepo.Object,
                Mock.Of<IAuthenticationDomainService>(),
                _refreshRepo.Object,
                _cache.Object,
                _activity.Object,
                NullLogger<Authentication.DomainService.Oidc.Services.IdpSessionService>.Instance);

        private static IdpSessionModel ActiveSession(string sessionId = "sess-1", params IdpSessionAccount[] accounts)
        {
            var list = accounts.Length > 0
                ? accounts.ToList()
                : new List<IdpSessionAccount> { new() { UserId = "user-1", TenantId = "tenant-1", LoginAt = DateTime.UtcNow } };
            return new IdpSessionModel
            {
                SessionId = sessionId,
                TenantId = "tenant-1",
                Accounts = list,
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                IdleExpiry = DateTime.UtcNow.AddHours(1),
                AbsoluteExpiry = DateTime.UtcNow.AddDays(30)
            };
        }

        // ---------- CreateSessionAsync ----------

        [Fact]
        public async Task CreateSessionAsync_PersistsSession_ReturnsGeneratedId()
        {
            _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>())).ReturnsAsync("ignored");

            var id = await Create().CreateSessionAsync("user-1", "tenant-1", "127.0.0.1");

            id.Should().NotBeNullOrEmpty();
            _sessionRepo.Verify(r => r.CreateAsync(It.Is<IdpSessionModel>(s =>
                s.TenantId == "tenant-1"
                && s.Accounts.Count == 1
                && s.Accounts[0].UserId == "user-1")), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        [Fact]
        public async Task CreateSessionAsync_Rethrows_WhenRepositoryFails()
        {
            _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>())).ThrowsAsync(new InvalidOperationException("db down"));

            var act = async () => await Create().CreateSessionAsync("user-1", "tenant-1", "ip");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // ---------- GetSessionAsync ----------

        [Fact]
        public async Task GetSessionAsync_ReturnsSessionFromRepository()
        {
            var session = ActiveSession("sess-x");
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-x")).ReturnsAsync(session);

            var result = await Create().GetSessionAsync("sess-x");

            result.Should().BeSameAs(session);
        }

        [Fact]
        public async Task GetSessionAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-x")).ThrowsAsync(new Exception("boom"));

            var act = async () => await Create().GetSessionAsync("sess-x");

            await act.Should().ThrowAsync<Exception>();
        }

        // ---------- AddAccountAsync ----------

        [Fact]
        public async Task AddAccountAsync_ReturnsFalse_WhenSessionMissing()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync((IdpSessionModel)null!);

            var result = await Create().AddAccountAsync("sess-1", "user-2", "tenant-1", "User Two");

            result.Should().BeFalse();
            _sessionRepo.Verify(r => r.AddAccountAsync(It.IsAny<string>(), It.IsAny<IdpSessionAccount>()), Times.Never);
        }

        [Fact]
        public async Task AddAccountAsync_ReturnsFalse_WhenSessionRevoked()
        {
            var revoked = ActiveSession();
            revoked.RevokedAt = DateTime.UtcNow;
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(revoked);

            var result = await Create().AddAccountAsync("sess-1", "user-2", "tenant-1", "User Two");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task AddAccountAsync_ReturnsTrue_WhenAccountAlreadyPresent()
        {
            var session = ActiveSession("sess-1",
                new IdpSessionAccount { UserId = "user-2", TenantId = "tenant-1", LoginAt = DateTime.UtcNow });
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(session);

            var result = await Create().AddAccountAsync("sess-1", "user-2", "tenant-1", "User Two");

            result.Should().BeTrue();
            _sessionRepo.Verify(r => r.AddAccountAsync(It.IsAny<string>(), It.IsAny<IdpSessionAccount>()), Times.Never);
        }

        [Fact]
        public async Task AddAccountAsync_AddsNewAccount_WhenNotPresent()
        {
            var session = ActiveSession("sess-1",
                new IdpSessionAccount { UserId = "user-1", TenantId = "tenant-1", LoginAt = DateTime.UtcNow });
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(session);
            _sessionRepo.Setup(r => r.AddAccountAsync("sess-1", It.IsAny<IdpSessionAccount>())).ReturnsAsync(true);

            var result = await Create().AddAccountAsync("sess-1", "user-2", "tenant-1", "User Two");

            result.Should().BeTrue();
            _sessionRepo.Verify(r => r.AddAccountAsync("sess-1", It.Is<IdpSessionAccount>(a => a.UserId == "user-2" && a.DisplayName == "User Two")), Times.Once);
        }

        // ---------- SelectAccountAsync ----------

        [Fact]
        public async Task SelectAccountAsync_ReturnsFalse_WhenSessionMissing()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync((IdpSessionModel)null!);

            var result = await Create().SelectAccountAsync("sess-1", "user-1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task SelectAccountAsync_ReturnsFalse_WhenAccountNotInSession()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(ActiveSession());

            var result = await Create().SelectAccountAsync("sess-1", "unknown-user");

            result.Should().BeFalse();
            _sessionRepo.Verify(r => r.UpdateActivityAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SelectAccountAsync_UpdatesActivity_WhenAccountPresent()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(ActiveSession());
            _sessionRepo.Setup(r => r.UpdateActivityAsync("sess-1")).ReturnsAsync(true);

            var result = await Create().SelectAccountAsync("sess-1", "user-1");

            result.Should().BeTrue();
            _sessionRepo.Verify(r => r.UpdateActivityAsync("sess-1"), Times.Once);
        }

        // ---------- RemoveAccountAsync ----------

        [Fact]
        public async Task RemoveAccountAsync_ReturnsFalse_WhenSessionMissing()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync((IdpSessionModel)null!);

            var result = await Create().RemoveAccountAsync("sess-1", "user-1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task RemoveAccountAsync_ReturnsFalse_WhenAccountNotFound()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(ActiveSession());

            var result = await Create().RemoveAccountAsync("sess-1", "nobody");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task RemoveAccountAsync_DeletesSession_WhenRemovingLastAccount()
        {
            var session = ActiveSession("sess-1",
                new IdpSessionAccount { UserId = "user-1", TenantId = "tenant-1", LoginAt = DateTime.UtcNow });
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(session);
            _sessionRepo.Setup(r => r.DeleteAsync("sess-1")).ReturnsAsync(true);

            var result = await Create().RemoveAccountAsync("sess-1", "user-1", "tenant-1");

            result.Should().BeTrue();
            _sessionRepo.Verify(r => r.DeleteAsync("sess-1"), Times.Once);
            _sessionRepo.Verify(r => r.RemoveAccountAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RemoveAccountAsync_RemovesSingleAccount_WhenMultiplePresent()
        {
            var session = ActiveSession("sess-1",
                new IdpSessionAccount { UserId = "user-1", TenantId = "tenant-1", LoginAt = DateTime.UtcNow },
                new IdpSessionAccount { UserId = "user-2", TenantId = "tenant-1", LoginAt = DateTime.UtcNow });
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(session);
            _sessionRepo.Setup(r => r.RemoveAccountAsync("sess-1", "user-1", "tenant-1")).ReturnsAsync(true);

            var result = await Create().RemoveAccountAsync("sess-1", "user-1", "tenant-1");

            result.Should().BeTrue();
            _sessionRepo.Verify(r => r.RemoveAccountAsync("sess-1", "user-1", "tenant-1"), Times.Once);
            _sessionRepo.Verify(r => r.DeleteAsync(It.IsAny<string>()), Times.Never);
        }

        // ---------- GetAccountsAsync ----------

        [Fact]
        public async Task GetAccountsAsync_ReturnsAccounts_WhenSessionExists()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(ActiveSession());

            var accounts = await Create().GetAccountsAsync("sess-1");

            accounts.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetAccountsAsync_ReturnsEmpty_WhenSessionMissing()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync((IdpSessionModel)null!);

            var accounts = await Create().GetAccountsAsync("sess-1");

            accounts.Should().BeEmpty();
        }

        // ---------- UpdateActivityAsync ----------

        [Fact]
        public async Task UpdateActivityAsync_ReturnsFalse_WhenSessionMissing()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync((IdpSessionModel)null!);

            (await Create().UpdateActivityAsync("sess-1")).Should().BeFalse();
        }

        [Fact]
        public async Task UpdateActivityAsync_ReturnsFalse_WhenSessionRevoked()
        {
            var revoked = ActiveSession();
            revoked.RevokedAt = DateTime.UtcNow;
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(revoked);

            (await Create().UpdateActivityAsync("sess-1")).Should().BeFalse();
        }

        [Fact]
        public async Task UpdateActivityAsync_ReturnsFalse_WhenExpired()
        {
            var expired = ActiveSession();
            expired.IdleExpiry = DateTime.UtcNow.AddMinutes(-5);
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(expired);

            (await Create().UpdateActivityAsync("sess-1")).Should().BeFalse();
            _sessionRepo.Verify(r => r.UpdateActivityAsync("sess-1"), Times.Never);
        }

        [Fact]
        public async Task UpdateActivityAsync_ReturnsTrue_WhenActive()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(ActiveSession());
            _sessionRepo.Setup(r => r.UpdateActivityAsync("sess-1")).ReturnsAsync(true);

            (await Create().UpdateActivityAsync("sess-1")).Should().BeTrue();
        }

        // ---------- RotateSessionAsync ----------

        [Fact]
        public async Task RotateSessionAsync_ReturnsNull_WhenSessionMissing()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync((IdpSessionModel)null!);

            (await Create().RotateSessionAsync("sess-1", "reason")).Should().BeNull();
        }

        [Fact]
        public async Task RotateSessionAsync_ReturnsNull_WhenExpired()
        {
            var expired = ActiveSession();
            expired.AbsoluteExpiry = DateTime.UtcNow.AddMinutes(-1);
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(expired);

            (await Create().RotateSessionAsync("sess-1", "reason")).Should().BeNull();
        }

        [Fact]
        public async Task RotateSessionAsync_CreatesNewSessionAndDeletesOld_WhenActive()
        {
            var session = ActiveSession("sess-old");
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-old")).ReturnsAsync(session);
            _sessionRepo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>())).ReturnsAsync("ignored");
            _sessionRepo.Setup(r => r.DeleteAsync("sess-old")).ReturnsAsync(true);

            var newId = await Create().RotateSessionAsync("sess-old", "login_success");

            newId.Should().NotBeNullOrEmpty();
            newId.Should().NotBe("sess-old");
            _sessionRepo.Verify(r => r.CreateAsync(It.Is<IdpSessionModel>(s => s.Accounts == session.Accounts)), Times.Once);
            _sessionRepo.Verify(r => r.DeleteAsync("sess-old"), Times.Once);
        }

        // ---------- RevokeSessionAsync ----------

        [Fact]
        public async Task RevokeSessionAsync_ReturnsFalse_WhenSessionMissing()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync((IdpSessionModel)null!);

            (await Create().RevokeSessionAsync("sess-1", "logout")).Should().BeFalse();
        }

        [Fact]
        public async Task RevokeSessionAsync_RevokesTokensAndDeletesSession_WhenPresent()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(ActiveSession());
            _refreshRepo.Setup(r => r.GetActiveTokensBySessionIdAsync("sess-1"))
                .ReturnsAsync((IReadOnlyList<RefreshTokenModel>)new List<RefreshTokenModel>
                {
                    new() { TokenId = "tok-1" },
                    new() { TokenId = "tok-2" }
                });
            _refreshRepo.Setup(r => r.RevokeAllBySessionIdAsync("sess-1", It.IsAny<string>())).ReturnsAsync(2);
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            _sessionRepo.Setup(r => r.DeleteAsync("sess-1")).ReturnsAsync(true);

            var result = await Create().RevokeSessionAsync("sess-1", "logout_all");

            result.Should().BeTrue();
            _refreshRepo.Verify(r => r.RevokeAllBySessionIdAsync("sess-1", "session_revoked:logout_all"), Times.Once);
            _cache.Verify(c => c.RemoveKeyAsync("tok-1"), Times.Once);
            _cache.Verify(c => c.RemoveKeyAsync("tok-2"), Times.Once);
            _sessionRepo.Verify(r => r.DeleteAsync("sess-1"), Times.Once);
        }

        // ---------- IsSessionActiveAsync ----------

        [Fact]
        public async Task IsSessionActiveAsync_ReturnsFalse_WhenSessionMissing()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync((IdpSessionModel)null!);

            (await Create().IsSessionActiveAsync("sess-1")).Should().BeFalse();
        }

        [Fact]
        public async Task IsSessionActiveAsync_ReturnsFalse_WhenRevoked()
        {
            var revoked = ActiveSession();
            revoked.RevokedAt = DateTime.UtcNow;
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(revoked);

            (await Create().IsSessionActiveAsync("sess-1")).Should().BeFalse();
        }

        [Fact]
        public async Task IsSessionActiveAsync_ReturnsFalse_WhenExpired()
        {
            var expired = ActiveSession();
            expired.IdleExpiry = DateTime.UtcNow.AddMinutes(-1);
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(expired);

            (await Create().IsSessionActiveAsync("sess-1")).Should().BeFalse();
        }

        [Fact]
        public async Task IsSessionActiveAsync_ReturnsTrue_WhenActive()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(ActiveSession());

            (await Create().IsSessionActiveAsync("sess-1")).Should().BeTrue();
        }

        // ---------- GetUserSessionsAsync ----------

        [Fact]
        public async Task GetUserSessionsAsync_ReturnsSessionsFromRepository()
        {
            var sessions = new List<IdpSessionModel> { ActiveSession("s1"), ActiveSession("s2") };
            _sessionRepo.Setup(r => r.GetByUserAsync("user-1", "tenant-1")).ReturnsAsync(sessions);

            var result = await Create().GetUserSessionsAsync("user-1", "tenant-1");

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetUserSessionsAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetByUserAsync("user-1", "tenant-1")).ThrowsAsync(new Exception("boom"));

            var act = async () => await Create().GetUserSessionsAsync("user-1", "tenant-1");

            await act.Should().ThrowAsync<Exception>();
        }

        // ---------- catch/rethrow blocks ----------

        [Fact]
        public async Task AddAccountAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ThrowsAsync(new InvalidOperationException("db"));

            var act = async () => await Create().AddAccountAsync("sess-1", "user-1", "tenant-1", "U");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task SelectAccountAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ThrowsAsync(new InvalidOperationException("db"));

            var act = async () => await Create().SelectAccountAsync("sess-1", "user-1");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task RemoveAccountAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ThrowsAsync(new InvalidOperationException("db"));

            var act = async () => await Create().RemoveAccountAsync("sess-1", "user-1");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task GetAccountsAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ThrowsAsync(new InvalidOperationException("db"));

            var act = async () => await Create().GetAccountsAsync("sess-1");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task UpdateActivityAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ThrowsAsync(new InvalidOperationException("db"));

            var act = async () => await Create().UpdateActivityAsync("sess-1");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task RotateSessionAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ThrowsAsync(new InvalidOperationException("db"));

            var act = async () => await Create().RotateSessionAsync("sess-1", "reason");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task RevokeSessionAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ThrowsAsync(new InvalidOperationException("db"));

            var act = async () => await Create().RevokeSessionAsync("sess-1", "reason");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task IsSessionActiveAsync_Rethrows_OnRepositoryError()
        {
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ThrowsAsync(new InvalidOperationException("db"));

            var act = async () => await Create().IsSessionActiveAsync("sess-1");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task AddAccountAsync_SwallowsAuditFailure_AndStillReturnsTrue()
        {
            // LogSessionEvent must never fail the operation: when the activity dispatcher throws,
            // the account add still succeeds. Exercises the LogSessionEvent catch block.
            _sessionRepo.Setup(r => r.GetBySessionIdAsync("sess-1")).ReturnsAsync(ActiveSession());
            _sessionRepo.Setup(r => r.AddAccountAsync("sess-1", It.IsAny<IdpSessionAccount>())).ReturnsAsync(true);
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).ThrowsAsync(new Exception("audit down"));

            var result = await Create().AddAccountAsync("sess-1", "user-2", "tenant-1", "U2");

            result.Should().BeTrue();
        }
    }
}
