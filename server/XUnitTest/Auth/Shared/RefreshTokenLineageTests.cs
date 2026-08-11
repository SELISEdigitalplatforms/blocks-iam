using System.Text.Json;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Covers issuance: lineage identity and the fixed absolute cap (SPEC2 H1, H2, H3, H7, C3, C5, C6),
    /// and the re-login supersession that ends a lineage the browser is no longer holding (SPEC3 H1–H3,
    /// H6, C1–C6).
    /// </summary>
    public sealed class RefreshTokenLineageTests
    {
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IAuthenticationDomainService> _authDomain = new();
        private readonly Mock<IRefreshTokenRepository> _repo = new();
        private readonly Mock<IUserActivityDispatcher> _dispatcher = new();
        private readonly Mock<IIdpSessionService> _idpSession = new();
        private readonly Mock<IHttpContextAccessor> _httpContext = new();
        private readonly Mock<IUserRepository> _users = new();

        private readonly List<RefreshTokenModel> _created = new();

        public RefreshTokenLineageTests()
        {
            _authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>())).Returns(new DeviceInformation { Device = "Test" });
            _idpSession.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("session-1");
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            _users.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(new User { ItemId = "user-1" });
            _repo.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenModel>()))
                .Callback<RefreshTokenModel>(m => _created.Add(m))
                .ReturnsAsync("ok");
        }

        private UnifiedTokenSessionService Sut() =>
            new(_cache.Object, _authDomain.Object, _repo.Object, _dispatcher.Object, _idpSession.Object,
                _httpContext.Object, _users.Object, NullLogger<UnifiedTokenSessionService>.Instance);

        private static Tenant Tenant() => XUnitTest.Auth.OAuth.RefreshTokenAuthenticationServiceTests.MakeTenant();

        private static User TestUser() => new() { ItemId = "user-1", TokenVersion = 1, UserName = "u", Email = "u@test.local" };

        private static TokenRequest Request(string grantType = "password") =>
            new() { ClientId = "client-1", GrantType = grantType, Scope = "openid", OrganizationId = "default" };

        private static IdentityConfiguration Config(int sliding = 60, int absolute = 10080) =>
            new() { RefreshTokenValidForNumberMinutes = sliding, AbsoluteRefreshTokenValidForNumberMinutes = absolute };

        private static RefreshTokenCache Predecessor(string tokenId, string? lineage, DateTime absoluteExpiry) =>
            new()
            {
                RefreshToken = tokenId,
                TenantId = "tenant-1",
                ClientId = "client-1",
                SessionId = "session-1",
                UserId = "user-1",
                RefreshTokenSessionId = lineage,
                IssuedUtc = DateTime.UtcNow.AddMinutes(-30),
                ExpiresUtc = DateTime.UtcNow.AddMinutes(30),
                AbsoluteExpiresUtc = absoluteExpiry
            };

        private Task<(string RefreshToken, DateTime SlidingExpiry, DateTime AbsoluteExpiry, string RefreshTokenSessionId)> Issue(
            string? oldToken = null,
            RefreshTokenCache? oldCache = null,
            IdentityConfiguration? config = null,
            string grantType = "password") =>
            Sut().CreateOrRotateRefreshToken(oldToken, oldCache, Request(grantType), config ?? Config(),
                Tenant(), TestUser(), new[] { "127.0.0.1" }, impersoanted: false);

        // ==================== SPEC2 H1 — a new authentication starts a lineage ====================

        [Fact]
        public async Task NewLineage_UsesItsOwnTokenIdAsLineageIdAndAnchorsTheCap()
        {
            var before = DateTime.UtcNow;

            var result = await Issue();

            result.RefreshTokenSessionId.Should().Be(result.RefreshToken);
            result.AbsoluteExpiry.Should().BeCloseTo(before.AddMinutes(10080), TimeSpan.FromMinutes(1));
            _created.Single().RefreshTokenSessionId.Should().Be(result.RefreshToken);
        }

        // ==================== SPEC2 H2 — rotation preserves the cap, resets idle ====================

        [Fact]
        public async Task Rotation_CopiesLineageAndCapButAdvancesTheSlidingWindow()
        {
            var cap = DateTime.UtcNow.AddDays(6);
            var result = await Issue("old-token", Predecessor("old-token", "lineage-1", cap));

            result.RefreshTokenSessionId.Should().Be("lineage-1");
            result.AbsoluteExpiry.Should().Be(cap);
            result.SlidingExpiry.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(60), TimeSpan.FromMinutes(1));
            result.RefreshToken.Should().NotBe("old-token");
        }

        [Fact]
        public async Task FiveConsecutiveRotations_KeepOneLineageAndOneUnchangedCap()
        {
            var cap = DateTime.UtcNow.AddDays(6);
            var token = "t0";
            var lineage = "lineage-1";

            for (var i = 0; i < 5; i++)
            {
                var result = await Issue(token, Predecessor(token, lineage, cap));
                result.RefreshTokenSessionId.Should().Be(lineage);
                result.AbsoluteExpiry.Should().Be(cap, "the cap is fixed at login and never extended by rotation");
                token = result.RefreshToken;
            }

            _created.Should().HaveCount(5);
            _created.Select(t => t.RefreshTokenSessionId).Should().AllBe(lineage);
            _created.Select(t => t.AbsoluteExpiry).Distinct().Should().ContainSingle();
        }

        // ==================== SPEC2 H7 — legacy documents ====================

        [Fact]
        public async Task RotatingALegacyTokenTreatsItAsALineageOfOne()
        {
            var cap = DateTime.UtcNow.AddDays(2);
            var result = await Issue("legacy-token", Predecessor("legacy-token", lineage: null, absoluteExpiry: cap));

            result.RefreshTokenSessionId.Should().Be("legacy-token");
            result.AbsoluteExpiry.Should().Be(cap, "a legacy cap is inherited, not re-anchored");
        }

        [Fact]
        public async Task RotationFallsBackToTheStoreWhenTheCacheEntryIsGone()
        {
            var cap = DateTime.UtcNow.AddDays(3);
            _repo.Setup(r => r.GetByTokenIdAsync("old-token")).ReturnsAsync(new RefreshTokenModel
            {
                TokenId = "old-token",
                UserId = "user-1",
                RefreshTokenSessionId = "lineage-9",
                AbsoluteExpiry = cap,
                SlidingExpiry = DateTime.UtcNow.AddMinutes(10)
            });

            var result = await Issue("old-token", oldCache: null);

            result.RefreshTokenSessionId.Should().Be("lineage-9");
            result.AbsoluteExpiry.Should().Be(cap);
        }

        // ==================== SPEC2 C3 — an unresolvable predecessor fails ====================

        [Fact]
        public async Task RotationWithAnUnresolvablePredecessor_FailsInsteadOfStartingANewLineage()
        {
            _repo.Setup(r => r.GetByTokenIdAsync("ghost")).ReturnsAsync((RefreshTokenModel)null!);

            var result = await Issue("ghost", oldCache: null);

            result.RefreshToken.Should().BeEmpty();
            _repo.Verify(r => r.CreateAsync(It.IsAny<RefreshTokenModel>()), Times.Never);
            _repo.Verify(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ==================== SPEC1 H1 — the successor pointer is written on rotation ====================

        [Fact]
        public async Task Rotation_StampsThePredecessorWithItsSuccessorId()
        {
            var result = await Issue("old-token", Predecessor("old-token", "lineage-1", DateTime.UtcNow.AddDays(6)));

            _repo.Verify(r => r.RevokeByTokenIdAsync("old-token", "superseded_by_rotation", result.RefreshToken), Times.Once);
        }

        // ==================== SPEC2 H3 / C5 — Redis TTL never outlives the cap ====================

        [Fact]
        public async Task CacheTtlIsTheSlidingWindowWhenTheCapIsFarAway()
        {
            var ttl = -1L;
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, _, seconds) => ttl = seconds)
                .ReturnsAsync(true);

            await Issue(config: Config(sliding: 60, absolute: 10080));

            ttl.Should().Be(3600);
        }

        [Fact]
        public async Task CacheTtlIsClampedToTheRemainingCapWhenTheLineageIsNearlyDone()
        {
            var ttl = -1L;
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, _, seconds) => ttl = seconds)
                .ReturnsAsync(true);

            await Issue("old-token", Predecessor("old-token", "lineage-1", DateTime.UtcNow.AddMinutes(30)),
                config: Config(sliding: 60));

            ttl.Should().BeInRange(1, 1800);
        }

        // ==================== SPEC2 C6 — defensive configuration ====================

        [Theory]
        [InlineData(300, 30)]   // absolute shorter than sliding
        [InlineData(0, 0)]      // both unset
        [InlineData(-5, -5)]    // both negative
        public async Task InvertedOrMissingLifetimes_DegradeInsteadOfFailingTheRequest(int sliding, int absolute)
        {
            var result = await Issue(config: Config(sliding, absolute));

            result.RefreshToken.Should().NotBeNullOrWhiteSpace();
            result.AbsoluteExpiry.Should().BeOnOrAfter(result.SlidingExpiry.AddSeconds(-1));
        }

        [Fact]
        public void Resolver_RaisesAnInvertedCapToTheSlidingWindow()
        {
            var (sliding, absolute) = RefreshTokenLifetimeResolver.Resolve(Config(sliding: 300, absolute: 30));

            sliding.Should().Be(300);
            absolute.Should().Be(300);
        }

        [Fact]
        public void Resolver_SubstitutesDocumentedDefaultsForNonPositiveValues()
        {
            var (sliding, absolute) = RefreshTokenLifetimeResolver.Resolve(Config(sliding: 0, absolute: 0));

            sliding.Should().Be(IdentityConfiguration.DefaultRefreshTokenValidForNumberMinutes);
            absolute.Should().Be(IdentityConfiguration.DefaultAbsoluteRefreshTokenValidForNumberMinutes);
        }

        // ==================== SPEC3 H1, H2, H3 — supersede on re-login ====================

        [Fact]
        public async Task NewLineage_SupersedesThePreviousLineageForTheSameBrowserAndApplication()
        {
            _repo.Setup(r => r.RevokeSupersededLoginLineagesAsync(
                "session-1", "user-1", "client-1", It.IsAny<string>(), "superseded_by_login")).ReturnsAsync(2);

            var result = await Issue();

            _repo.Verify(r => r.RevokeSupersededLoginLineagesAsync(
                "session-1", "user-1", "client-1", result.RefreshTokenSessionId, "superseded_by_login"), Times.Once);
        }

        [Fact]
        public async Task NewLineage_PersistsTheNewTokenBeforeSupersedingAnything()
        {
            var order = new List<string>();
            _repo.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenModel>()))
                .Callback<RefreshTokenModel>(m => { _created.Add(m); order.Add("create"); })
                .ReturnsAsync("ok");
            _repo.Setup(r => r.RevokeSupersededLoginLineagesAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => order.Add("supersede"))
                .ReturnsAsync(1);

            await Issue();

            order.Should().Equal("create", "supersede");
        }

        [Fact]
        public async Task Rotation_NeverSupersedesByLogin()
        {
            await Issue("old-token", Predecessor("old-token", "lineage-1", DateTime.UtcNow.AddDays(6)));

            _repo.Verify(r => r.RevokeSupersededLoginLineagesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Rotation_DoesNotCountAsALoginForTheUserCounters()
        {
            await Issue("old-token", Predecessor("old-token", "lineage-1", DateTime.UtcNow.AddDays(6)));

            _users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        // ==================== SPEC3 C4, C5 — supersession is best-effort bookkeeping ====================

        [Fact]
        public async Task NothingToSupersede_CompletesTheLoginNormally()
        {
            _repo.Setup(r => r.RevokeSupersededLoginLineagesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(0);

            var result = await Issue();

            result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task SupersessionFailure_IsSwallowedSoTheLoginStillReturnsTokens()
        {
            _repo.Setup(r => r.RevokeSupersededLoginLineagesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new TimeoutException("mongo down"));

            var result = await Issue();

            result.RefreshToken.Should().NotBeNullOrWhiteSpace();
            result.RefreshTokenSessionId.Should().Be(result.RefreshToken);
        }
    }
}
