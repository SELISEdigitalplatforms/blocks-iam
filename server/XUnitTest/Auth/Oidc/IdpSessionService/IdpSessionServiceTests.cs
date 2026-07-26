using Microsoft.AspNetCore.Http;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Oidc.IdpSessionService
{
    public class IdpSessionServiceTests
    {
        private static Authentication.DomainService.Oidc.Services.IdpSessionService CreateService(
            out Mock<IIdpSessionRepository> sessionRepo,
            out Mock<IRefreshTokenRepository> refreshRepo,
            out Mock<ICacheClient> cache)
        {
            sessionRepo = new Mock<IIdpSessionRepository>();
            refreshRepo = new Mock<IRefreshTokenRepository>();
            cache = new Mock<ICacheClient>();

            return new Authentication.DomainService.Oidc.Services.IdpSessionService(
                sessionRepo.Object,
                Mock.Of<IAuthenticationDomainService>(),
                refreshRepo.Object,
                cache.Object,
                Mock.Of<IUserActivityDispatcher>(),
                NullLogger<Authentication.DomainService.Oidc.Services.IdpSessionService>.Instance);
        }

        [Fact]
        public async Task ResolveOrCreateAsync_NoCookie_CreatesNewSessionAndWritesCookie()
        {
            var service = CreateService(out var sessionRepo, out _, out _);

            sessionRepo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>()))
                .ReturnsAsync((IdpSessionModel m) => m.SessionId);

            var ctx = new DefaultHttpContext();
            var sessionId = await service.ResolveOrCreateAsync(ctx, "user-1", "tenant-1", "127.0.0.1");

            sessionId.Should().NotBeNullOrEmpty();
            sessionRepo.Verify(r => r.CreateAsync(It.IsAny<IdpSessionModel>()), Times.Once);
            ctx.Response.Headers.ContainsKey("Set-Cookie").Should().BeTrue();
        }

        [Fact]
        public async Task ResolveOrCreateAsync_ExistingValidCookie_ReusesAndDoesNotRecreate()
        {
            var service = CreateService(out var sessionRepo, out _, out _);

            var existing = new IdpSessionModel
            {
                SessionId = "session-existing",
                TenantId = "tenant-1",
                Accounts = new List<IdpSessionAccount>
                {
                    new() { UserId = "user-1", TenantId = "tenant-1", LoginAt = DateTime.UtcNow }
                },
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                IdleExpiry = DateTime.UtcNow.AddHours(1),
                AbsoluteExpiry = DateTime.UtcNow.AddDays(30)
            };

            sessionRepo.Setup(r => r.GetBySessionIdAsync("session-existing")).ReturnsAsync(existing);
            sessionRepo.Setup(r => r.UpdateActivityAsync("session-existing")).ReturnsAsync(true);

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = StubCookieCollection(("idp_session_id_tenant-1", "session-existing"));

            var sessionId = await service.ResolveOrCreateAsync(ctx, "user-1", "tenant-1", "127.0.0.1");

            sessionId.Should().Be("session-existing");
            sessionRepo.Verify(r => r.CreateAsync(It.IsAny<IdpSessionModel>()), Times.Never);
            sessionRepo.Verify(r => r.UpdateActivityAsync("session-existing"), Times.Once);
        }

        [Fact]
        public async Task ResolveOrCreateAsync_RevokedCookie_CreatesNewSession()
        {
            var service = CreateService(out var sessionRepo, out _, out _);

            var revoked = new IdpSessionModel
            {
                SessionId = "session-revoked",
                TenantId = "tenant-1",
                Accounts = new List<IdpSessionAccount>
                {
                    new() { UserId = "user-1", TenantId = "tenant-1", LoginAt = DateTime.UtcNow }
                },
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                IdleExpiry = DateTime.UtcNow.AddHours(1),
                AbsoluteExpiry = DateTime.UtcNow.AddDays(30),
                RevokedAt = DateTime.UtcNow
            };

            sessionRepo.Setup(r => r.GetBySessionIdAsync("session-revoked")).ReturnsAsync(revoked);
            sessionRepo.Setup(r => r.CreateAsync(It.IsAny<IdpSessionModel>()))
                .ReturnsAsync((IdpSessionModel m) => m.SessionId);

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = StubCookieCollection(("idp_session_id_tenant-1", "session-revoked"));

            var newSessionId = await service.ResolveOrCreateAsync(ctx, "user-1", "tenant-1", "127.0.0.1");

            newSessionId.Should().NotBe("session-revoked");
            sessionRepo.Verify(r => r.CreateAsync(It.IsAny<IdpSessionModel>()), Times.Once);
        }

        [Fact]
        public async Task ResolveOrCreateAsync_ExistingValidCookie_AccountNotInSession_AddsAccountAndReuses()
        {
            var service = CreateService(out var sessionRepo, out _, out _);

            var existing = new IdpSessionModel
            {
                SessionId = "session-existing",
                TenantId = "tenant-1",
                Accounts = new List<IdpSessionAccount>
                {
                    new() { UserId = "other-user", TenantId = "tenant-1", LoginAt = DateTime.UtcNow }
                },
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                IdleExpiry = DateTime.UtcNow.AddHours(1),
                AbsoluteExpiry = DateTime.UtcNow.AddDays(30)
            };

            sessionRepo.Setup(r => r.GetBySessionIdAsync("session-existing")).ReturnsAsync(existing);
            sessionRepo.Setup(r => r.AddAccountAsync("session-existing", It.IsAny<IdpSessionAccount>())).ReturnsAsync(true);

            var ctx = new DefaultHttpContext();
            ctx.Request.Cookies = StubCookieCollection(("idp_session_id_tenant-1", "session-existing"));

            var sessionId = await service.ResolveOrCreateAsync(ctx, "user-1", "tenant-1", "127.0.0.1");

            sessionId.Should().Be("session-existing");
            sessionRepo.Verify(r => r.AddAccountAsync("session-existing",
                It.Is<IdpSessionAccount>(a => a.UserId == "user-1" && a.TenantId == "tenant-1")), Times.Once);
            sessionRepo.Verify(r => r.CreateAsync(It.IsAny<IdpSessionModel>()), Times.Never);
        }

        [Theory]
        [InlineData("", "tenant-1")]
        [InlineData("user-1", "")]
        public async Task ResolveOrCreateAsync_MissingUserOrTenant_Throws(string userId, string tenantId)
        {
            var service = CreateService(out _, out _, out _);

            var act = async () => await service.ResolveOrCreateAsync(new DefaultHttpContext(), userId, tenantId, "127.0.0.1");

            await act.Should().ThrowAsync<ArgumentException>();
        }

        private static IRequestCookieCollection StubCookieCollection(params (string Key, string Value)[] entries)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in entries) dict[e.Key] = e.Value;
            return new StubRequestCookieCollection(dict);
        }

        private sealed class StubRequestCookieCollection : IRequestCookieCollection
        {
            private readonly Dictionary<string, string> _values;
            public StubRequestCookieCollection(Dictionary<string, string> values) => _values = values;
            public string? this[string key] => _values.TryGetValue(key, out var v) ? v : null;
            public int Count => _values.Count;
            public ICollection<string> Keys => _values.Keys;
            public bool ContainsKey(string key) => _values.ContainsKey(key);
            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            public bool TryGetValue(string key, out string? value)
            {
                if (_values.TryGetValue(key, out var v)) { value = v; return true; }
                value = null; return false;
            }
        }
    }
}
