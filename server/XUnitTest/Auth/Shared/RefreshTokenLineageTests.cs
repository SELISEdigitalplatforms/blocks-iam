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
using Microsoft.Extensions.Logging;
using Moq;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Covers issuance: lineage identity and the fixed absolute cap (SPEC2 H1, H2, H3, H7, C3, C5, C6),
    /// the re-login supersession that ends a lineage the browser is no longer holding (SPEC3 H1–H3,
    /// H6, C1–C6), and the lineage's OIDC client surviving every rotation (blocks-iam#624 H7, H10, H11,
    /// C1, C2, C4, C11, C13).
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
        private readonly List<string> _cachedJson = new();
        private readonly CapturingLogger _log = new();

        public RefreshTokenLineageTests()
        {
            _authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>())).Returns(new DeviceInformation { Device = "Test" });
            _idpSession.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("session-1");
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, json, _) => _cachedJson.Add(json))
                .ReturnsAsync(true);
            _cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
            _users.Setup(u => u.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(new User { ItemId = "user-1" });
            _repo.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenModel>()))
                .Callback<RefreshTokenModel>(m => _created.Add(m))
                .ReturnsAsync("ok");
        }

        private UnifiedTokenSessionService Sut() =>
            new(_cache.Object, _authDomain.Object, _repo.Object, _dispatcher.Object, _idpSession.Object,
                _httpContext.Object, _users.Object, _log);

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

        // ==================== blocks-iam#624 — a rotation keeps the lineage's OIDC client ====================
        //
        // switch-org and /auth/refresh rotated with a TokenRequest that carried no ClientId, and the
        // successor was stored client-less, so the next /oidc/token refresh failed invalid_client. The
        // rule lives in CreateOrRotateRefreshToken itself, so these tests drive it directly, for both a
        // predecessor answered by Redis and one answered by MongoDB.

        private const string LongOldToken = "0123456789abcdef0123456789abcdef";
        private static readonly string[] VisitorIps = ["127.0.0.1"];

        private void SeedPredecessor(string tokenId, string? clientId, bool predecessorCached, out RefreshTokenCache? oldCache)
        {
            if (predecessorCached)
            {
                oldCache = Predecessor(tokenId, "lineage-1", DateTime.UtcNow.AddDays(6));
                oldCache.ClientId = clientId;
                return;
            }

            oldCache = null;
            _repo.Setup(r => r.GetByTokenIdAsync(tokenId)).ReturnsAsync(new RefreshTokenModel
            {
                TokenId = tokenId,
                UserId = "user-1",
                TenantId = "tenant-1",
                SessionId = "session-1",
                ClientId = clientId!,
                RefreshTokenSessionId = "lineage-1",
                AbsoluteExpiry = DateTime.UtcNow.AddDays(6),
                SlidingExpiry = DateTime.UtcNow.AddMinutes(10)
            });
        }

        private Task<(string RefreshToken, DateTime SlidingExpiry, DateTime AbsoluteExpiry, string RefreshTokenSessionId)> Rotate(
            string oldToken,
            RefreshTokenCache? oldCache,
            string? requestedClientId,
            string grantType = "refresh_token",
            string organizationId = "default") =>
            Sut().CreateOrRotateRefreshToken(
                oldToken,
                oldCache,
                new TokenRequest { ClientId = requestedClientId, GrantType = grantType, OrganizationId = organizationId },
                Config(),
                Tenant(),
                TestUser(),
                VisitorIps,
                impersoanted: false);

        private RefreshTokenCache LastCached() => JsonSerializer.Deserialize<RefreshTokenCache>(_cachedJson.Last())!;

        private IEnumerable<CapturedLog> MismatchWarnings() =>
            _log.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains("reason=client_mismatch", StringComparison.Ordinal));

        // H10, H11, C2, C4 and the equal-client row: every row of the resolution table that is not a mismatch.
        [Theory]
        [InlineData("C1", null, "C1", true)]    // H10: request names no client -> lineage client
        [InlineData("C1", null, "C1", false)]   // H11: predecessor answered by MongoDB
        [InlineData("C1", "", "C1", true)]
        [InlineData("C1", "", "C1", false)]
        [InlineData("C1", "C1", "C1", true)]    // equal clients: no warning
        [InlineData("C1", "C1", "C1", false)]
        [InlineData("", "C2", "C2", true)]      // C4: client-less lineage adopts the request's client, as today
        [InlineData("", "C2", "C2", false)]
        [InlineData(null, "C2", "C2", true)]
        [InlineData(null, "C2", "C2", false)]
        public async Task Rotation_ResolvesTheSuccessorClientFromTheLineageWithoutWarning(
            string? lineageClient, string? requestedClient, string expected, bool predecessorCached)
        {
            SeedPredecessor(LongOldToken, lineageClient, predecessorCached, out var oldCache);

            var result = await Rotate(LongOldToken, oldCache, requestedClient);

            result.RefreshToken.Should().NotBeNullOrWhiteSpace();
            _created.Single().ClientId.Should().Be(expected, "the MongoDB successor carries the resolved client");
            LastCached().ClientId.Should().Be(expected, "the Redis successor carries the resolved client");
            MismatchWarnings().Should().BeEmpty();
        }

        // C2: an already client-less lineage stays client-less; nothing is repaired.
        [Theory]
        [InlineData(null, null, true)]
        [InlineData(null, null, false)]
        [InlineData("", "", true)]
        [InlineData("", "", false)]
        [InlineData("", null, true)]
        [InlineData(null, "", false)]
        public async Task Rotation_OfAClientLessLineageWithNoRequestedClient_StaysClientLess(
            string? lineageClient, string? requestedClient, bool predecessorCached)
        {
            SeedPredecessor(LongOldToken, lineageClient, predecessorCached, out var oldCache);

            var result = await Rotate(LongOldToken, oldCache, requestedClient);

            result.RefreshToken.Should().NotBeNullOrWhiteSpace();
            _created.Single().ClientId.Should().BeNullOrEmpty();
            LastCached().ClientId.Should().BeNullOrEmpty();
            MismatchWarnings().Should().BeEmpty();
        }

        // C1 and C13: a mismatching request client loses to the lineage, with exactly one warning that
        // carries no raw token value.
        [Theory]
        [InlineData("C1", "C2", true)]
        [InlineData("C1", "C2", false)]
        [InlineData("C1", "c1", true)]   // ordinal: a case-only difference is still a mismatch
        [InlineData("C1", "c1", false)]
        public async Task Rotation_WithAMismatchingRequestedClient_KeepsTheLineageClientAndWarnsOnce(
            string lineageClient, string requestedClient, bool predecessorCached)
        {
            SeedPredecessor(LongOldToken, lineageClient, predecessorCached, out var oldCache);

            var result = await Rotate(LongOldToken, oldCache, requestedClient);

            _created.Single().ClientId.Should().Be(lineageClient);
            LastCached().ClientId.Should().Be(lineageClient);

            var warning = MismatchWarnings().Should().ContainSingle().Subject;
            warning.State["RefreshTokenSessionId"].Should().Be("lineage-1");
            warning.State["LineageClientId"].Should().Be(lineageClient);
            warning.State["RequestedClientId"].Should().Be(requestedClient);
            var fingerprint = warning.State["TokenFingerprint"].Should().BeOfType<string>().Subject;
            fingerprint.Length.Should().BeLessThanOrEqualTo(8);
            LongOldToken.Should().StartWith(fingerprint);

            // C13: neither the presented token nor the minted successor appears anywhere in the logs.
            foreach (var entry in _log.Entries)
            {
                entry.Message.Should().NotContain(LongOldToken).And.NotContain(result.RefreshToken);
                entry.State.Values.OfType<string>().Should().NotContain(v => v.Contains(LongOldToken) || v.Contains(result.RefreshToken));
            }
        }

        [Fact]
        public async Task Rotation_WithAShortTokenId_FingerprintsTheWholeIdWithoutPadding()
        {
            SeedPredecessor("short", "C1", predecessorCached: true, out var oldCache);

            await Rotate("short", oldCache, "C2");

            MismatchWarnings().Should().ContainSingle().Which.State["TokenFingerprint"].Should().Be("short");
        }

        [Fact]
        public async Task FirstIssuance_UsesTheRequestedClientAndNeverConsultsAPredecessor()
        {
            await Issue();

            _created.Single().ClientId.Should().Be("client-1");
            LastCached().ClientId.Should().Be("client-1");
            _repo.Verify(r => r.GetByTokenIdAsync(It.IsAny<string>()), Times.Never);
            MismatchWarnings().Should().BeEmpty();
        }

        // H7 + H8: five hops through a mix of /oidc/token (names the client), switch-org and /auth/refresh
        // (which, at this layer, may name none) keep one client, one lineage and one cap. Each hop's
        // predecessor is the previous hop's Redis entry, exactly as ManageRefreshTokenAsync reads it.
        [Fact]
        public async Task FiveMixedRotations_KeepOneClientOneLineageAndOneCap()
        {
            var cap = DateTime.UtcNow.AddDays(6);
            var predecessor = Predecessor("t0", "lineage-1", cap);
            predecessor.ClientId = "C1";
            var hops = new (string Grant, string? Client, string Org)[]
            {
                ("refresh_token", "C1", "orgA"),         // /oidc/token
                ("switch_organization", null, "orgB"),   // /auth/switch-org
                ("refresh_token", null, "orgB"),         // /auth/refresh
                ("refresh_token", "C1", "orgB"),         // /oidc/token
                ("switch_organization", null, "orgA"),   // /auth/switch-org
            };

            foreach (var (grant, client, org) in hops)
            {
                var result = await Rotate(predecessor.RefreshToken!, predecessor, client, grant, org);
                result.RefreshTokenSessionId.Should().Be("lineage-1");
                result.AbsoluteExpiry.Should().Be(cap);
                predecessor = LastCached();
                predecessor.OrganizationId.Should().Be(org);
            }

            _created.Should().HaveCount(5);
            _created.Select(t => t.ClientId).Should().AllBe("C1");
            _cachedJson.Select(j => JsonSerializer.Deserialize<RefreshTokenCache>(j)!.ClientId).Should().AllBe("C1");
            _created.Select(t => t.RefreshTokenSessionId).Should().AllBe("lineage-1");
            MismatchWarnings().Should().BeEmpty();
        }

        // C11: a failed MongoDB write fails the rotation and leaves the predecessor live.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Rotation_WhenTheStoreWriteFails_FailsAndDoesNotRevokeThePredecessor(bool predecessorCached)
        {
            SeedPredecessor(LongOldToken, "C1", predecessorCached, out var oldCache);
            _repo.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenModel>())).ThrowsAsync(new TimeoutException("mongo down"));

            var act = () => Rotate(LongOldToken, oldCache, requestedClientId: null);

            await act.Should().ThrowAsync<TimeoutException>();
            _repo.Verify(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _cache.Verify(c => c.RemoveKeyAsync(LongOldToken), Times.Never);
        }

        /// <summary>
        /// Runs one real rotation of <paramref name="predecessor"/> (answered by Redis) through
        /// <see cref="UnifiedTokenSessionService"/> and returns the successor exactly as it was written to
        /// MongoDB and to Redis. Shared with the /oidc/token, revocation and supersession tests so they
        /// consume a successor produced by the real rotation rule rather than a hand-built one.
        /// </summary>
        internal static async Task<(RefreshTokenModel Stored, RefreshTokenCache Cached)> RotateOnceAsync(
            RefreshTokenCache predecessor,
            TokenRequest request)
        {
            var harness = new RefreshTokenLineageTests();
            var result = await harness.Sut().CreateOrRotateRefreshToken(
                predecessor.RefreshToken, predecessor, request, Config(), Tenant(), TestUser(),
                VisitorIps, impersoanted: false).ConfigureAwait(false);
            result.RefreshToken.Should().NotBeNullOrWhiteSpace();
            return (harness._created.Single(), harness.LastCached());
        }

        /// <summary>A hosted-OIDC predecessor as Redis holds it: client "client-1", IdP session "session-1".</summary>
        internal static RefreshTokenCache HostedPredecessor(string tokenId = "old-token") =>
            Predecessor(tokenId, "lineage-1", DateTime.UtcNow.AddDays(6));

        private sealed record CapturedLog(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State);

        private sealed class CapturingLogger : ILogger<UnifiedTokenSessionService>
        {
            public List<CapturedLog> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // A template may repeat a placeholder name; the first value wins.
                var values = new Dictionary<string, object?>();
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var (key, value) in pairs)
                    {
                        values.TryAdd(key, value);
                    }
                }

                Entries.Add(new CapturedLog(logLevel, formatter(state, exception), values));
            }
        }
    }
}
