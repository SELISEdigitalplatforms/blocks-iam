using System.Text.Json;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Covers the shared refresh-token validity check: the rotation grace window (SPEC1 H2–H7, C1–C7)
    /// and the enforced sliding/absolute lifetimes with lineage-scoped reuse handling (SPEC2 C1, C2, C4).
    /// Time is controlled by writing explicit expiry and revocation timestamps rather than sleeping.
    /// </summary>
    public sealed class RefreshSessionResolverTests
    {
        private const string Rotation = "superseded_by_rotation";

        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IRefreshTokenRepository> _repo = new();

        private RefreshSessionResolver Sut() =>
            new(_cache.Object, _repo.Object, NullLogger<RefreshSessionResolver>.Instance);

        private static IdentityConfiguration Config(int graceMinutes = 5, int sliding = 30, int absolute = 10080) =>
            new()
            {
                RefreshTokenValidForNumberMinutes = sliding,
                AbsoluteRefreshTokenValidForNumberMinutes = absolute,
                TokenRotationGracePeriodMinutes = graceMinutes
            };

        private static RefreshTokenModel Token(
            string id,
            bool isRevoked = false,
            string? revokeReason = null,
            DateTime? revokedAt = null,
            string? supersededBy = null,
            DateTime? slidingExpiry = null,
            DateTime? absoluteExpiry = null,
            string? lineage = "lineage-1") => new()
            {
                TokenId = id,
                UserId = "user-1",
                TenantId = "tenant-1",
                ClientId = "client-1",
                SessionId = "session-1",
                RefreshTokenSessionId = lineage,
                IssuedUtc = DateTime.UtcNow.AddMinutes(-10),
                SlidingExpiry = slidingExpiry ?? DateTime.UtcNow.AddMinutes(30),
                AbsoluteExpiry = absoluteExpiry ?? DateTime.UtcNow.AddDays(6),
                IsRevoked = isRevoked,
                RevokeReason = revokeReason,
                RevokedAt = revokedAt,
                SupersededByTokenId = supersededBy
            };

        private void CacheMiss() =>
            _cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string)null!);

        private void Persisted(params RefreshTokenModel[] tokens)
        {
            foreach (var token in tokens)
            {
                var captured = token;
                _repo.Setup(r => r.GetByTokenIdAsync(captured.TokenId)).ReturnsAsync(captured);
            }
        }

        // ==================== Cache hit ====================

        [Fact]
        public async Task CacheHit_Live_IsUsedWithoutTouchingTheStore()
        {
            var entry = new RefreshTokenCache
            {
                RefreshToken = "A",
                UserId = "user-1",
                ExpiresUtc = DateTime.UtcNow.AddMinutes(10),
                AbsoluteExpiresUtc = DateTime.UtcNow.AddDays(1)
            };
            _cache.Setup(c => c.GetStringValueAsync("A")).ReturnsAsync(JsonSerializer.Serialize(entry));

            var result = await Sut().TryResolveRefreshSessionAsync("A", Config());

            result.Should().NotBeNull();
            result!.RefreshToken.Should().Be("A");
            _repo.Verify(r => r.GetByTokenIdAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CacheHit_PastSlidingWindow_IsRejected()
        {
            var entry = new RefreshTokenCache
            {
                RefreshToken = "A",
                ExpiresUtc = DateTime.UtcNow.AddMinutes(-1),
                AbsoluteExpiresUtc = DateTime.UtcNow.AddDays(1)
            };
            _cache.Setup(c => c.GetStringValueAsync("A")).ReturnsAsync(JsonSerializer.Serialize(entry));

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
        }

        // ==================== SPEC2 C1 / C2 — enforced lifetimes on cache miss ====================

        [Fact]
        public async Task CacheMiss_PastSlidingExpiry_IsRejectedEvenWhileInsideTheAbsoluteCap()
        {
            CacheMiss();
            Persisted(Token("A",
                slidingExpiry: DateTime.UtcNow.AddMinutes(-1),
                absoluteExpiry: DateTime.UtcNow.AddDays(6)));

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _cache.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task CacheMiss_PastAbsoluteExpiry_IsRejectedHoweverRecentlyUsed()
        {
            CacheMiss();
            Persisted(Token("A",
                slidingExpiry: DateTime.UtcNow.AddMinutes(30),
                absoluteExpiry: DateTime.UtcNow.AddMinutes(-1)));

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
        }

        [Fact]
        public async Task CacheMiss_Live_IsRehydratedWithTtlClampedToRemainingAbsoluteLifetime()
        {
            var ttl = -1L;
            CacheMiss();
            Persisted(Token("A", absoluteExpiry: DateTime.UtcNow.AddSeconds(45)));
            _cache.Setup(c => c.AddStringValueAsync("A", It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, _, seconds) => ttl = seconds)
                .ReturnsAsync(true);

            var result = await Sut().TryResolveRefreshSessionAsync("A", Config());

            result.Should().NotBeNull();
            ttl.Should().BeInRange(1, 45);
        }

        [Fact]
        public async Task CacheMiss_NotFound_IsRejectedWithoutRevokingAnything()
        {
            CacheMiss();
            _repo.Setup(r => r.GetByTokenIdAsync("A")).ReturnsAsync((RefreshTokenModel)null!);

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ==================== SPEC1 H2, H3, H5 — grace-window replay ====================

        [Fact]
        public async Task InsideGrace_ResolvesToTheSuccessorWithoutRotating()
        {
            CacheMiss();
            Persisted(
                Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: DateTime.UtcNow.AddMinutes(-1), supersededBy: "B"),
                Token("B"));
            _cache.Setup(c => c.AddStringValueAsync("B", It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var result = await Sut().TryResolveRefreshSessionAsync("A", Config());

            result.Should().NotBeNull();
            result!.RefreshToken.Should().Be("B");
            // A retry is an already-counted use: nothing is revoked and no new token is minted.
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.CreateAsync(It.IsAny<RefreshTokenModel>()), Times.Never);
        }

        [Fact]
        public async Task InsideGrace_RehydratesTheSuccessorNotThePresentedToken()
        {
            CacheMiss();
            Persisted(
                Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: DateTime.UtcNow.AddMinutes(-1), supersededBy: "B"),
                Token("B"));
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            await Sut().TryResolveRefreshSessionAsync("A", Config());

            _cache.Verify(c => c.AddStringValueAsync("B", It.IsAny<string>(), It.IsAny<long>()), Times.Once);
            _cache.Verify(c => c.AddStringValueAsync("A", It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        // ==================== SPEC1 H6 — chain following ====================

        [Fact]
        public async Task InsideGrace_FollowsTheChainToTheFirstUnrevokedToken()
        {
            CacheMiss();
            var justNow = DateTime.UtcNow.AddMinutes(-1);
            Persisted(
                Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: justNow, supersededBy: "B"),
                Token("B", isRevoked: true, revokeReason: Rotation, revokedAt: justNow, supersededBy: "C"),
                Token("C"));
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var result = await Sut().TryResolveRefreshSessionAsync("A", Config());

            result!.RefreshToken.Should().Be("C");
        }

        [Fact]
        public async Task InsideGrace_ChainLongerThanTheHopLimit_IsRejected()
        {
            CacheMiss();
            var justNow = DateTime.UtcNow.AddMinutes(-1);
            // 12 links, all superseded — past the 10-hop budget before any live token is reached.
            for (var i = 0; i < 12; i++)
            {
                Persisted(Token($"t{i}", isRevoked: true, revokeReason: Rotation, revokedAt: justNow, supersededBy: $"t{i + 1}"));
            }
            Persisted(Token("t12"));

            (await Sut().TryResolveRefreshSessionAsync("t0", Config())).Should().BeNull();
        }

        [Fact]
        public async Task InsideGrace_CyclicChain_FailsClosedRatherThanLooping()
        {
            CacheMiss();
            var justNow = DateTime.UtcNow.AddMinutes(-1);
            Persisted(
                Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: justNow, supersededBy: "B"),
                Token("B", isRevoked: true, revokeReason: Rotation, revokedAt: justNow, supersededBy: "A"));

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
        }

        // ==================== SPEC1 C3 — dead ends revoke nothing ====================

        [Fact]
        public async Task InsideGrace_SuccessorMissing_IsRejectedAndRevokesNothing()
        {
            CacheMiss();
            Persisted(Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: DateTime.UtcNow.AddMinutes(-1), supersededBy: "B"));
            _repo.Setup(r => r.GetByTokenIdAsync("B")).ReturnsAsync((RefreshTokenModel)null!);

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InsideGrace_SuccessorLoggedOut_IsRejectedAndTheLogoutStands()
        {
            CacheMiss();
            Persisted(
                Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: DateTime.UtcNow.AddMinutes(-1), supersededBy: "B"),
                Token("B", isRevoked: true, revokeReason: "logout", revokedAt: DateTime.UtcNow.AddSeconds(-30)));

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InsideGrace_SuccessorPastItsAbsoluteCap_IsRejected()
        {
            CacheMiss();
            Persisted(
                Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: DateTime.UtcNow.AddMinutes(-1), supersededBy: "B"),
                Token("B", absoluteExpiry: DateTime.UtcNow.AddMinutes(-1)));

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ==================== SPEC1 C1 / SPEC2 C4 — replay past the grace window ====================

        [Fact]
        public async Task PastGrace_RevokesTheWholeLineageAndRejects()
        {
            CacheMiss();
            Persisted(Token("A", isRevoked: true, revokeReason: Rotation,
                revokedAt: DateTime.UtcNow.AddMinutes(-6), supersededBy: "B"));
            _repo.Setup(r => r.RevokeAllByRefreshTokenSessionIdAsync("lineage-1", "token_reuse_detected")).ReturnsAsync(3);
            _cache.Setup(c => c.RemoveKeyAsync("A")).ReturnsAsync(true);

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync("lineage-1", "token_reuse_detected"), Times.Once);
            _cache.Verify(c => c.RemoveKeyAsync("A"), Times.Once);
        }

        [Fact]
        public async Task PastGrace_LegacyDocumentWithoutLineage_RevokesByItsOwnTokenId()
        {
            CacheMiss();
            Persisted(Token("A", isRevoked: true, revokeReason: Rotation,
                revokedAt: DateTime.UtcNow.AddMinutes(-6), supersededBy: "B", lineage: null));
            _repo.Setup(r => r.RevokeAllByRefreshTokenSessionIdAsync("A", "token_reuse_detected")).ReturnsAsync(1);
            _cache.Setup(c => c.RemoveKeyAsync("A")).ReturnsAsync(true);

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync("A", "token_reuse_detected"), Times.Once);
        }

        // ==================== SPEC1 C2 — no successor pointer ====================

        [Fact]
        public async Task InsideGrace_NullSupersededByTokenId_IsTreatedAsReplayAndGuessesNothing()
        {
            CacheMiss();
            Persisted(Token("A", isRevoked: true, revokeReason: Rotation,
                revokedAt: DateTime.UtcNow.AddMinutes(-1), supersededBy: null));
            _repo.Setup(r => r.RevokeAllByRefreshTokenSessionIdAsync("lineage-1", "token_reuse_detected")).ReturnsAsync(1);
            _cache.Setup(c => c.RemoveKeyAsync("A")).ReturnsAsync(true);

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync("lineage-1", "token_reuse_detected"), Times.Once);
        }

        // ==================== SPEC1 C4 — other revocation reasons never escalate ====================

        [Theory]
        [InlineData("logout")]
        [InlineData("password_change")]
        [InlineData("superseded_by_login")]
        public async Task RevokedForAnotherReason_IsRejectedWithoutReuseHandling(string reason)
        {
            CacheMiss();
            Persisted(Token("A", isRevoked: true, revokeReason: reason, revokedAt: DateTime.UtcNow.AddMinutes(-1)));

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.GetByTokenIdAsync("B"), Times.Never);
        }

        // ==================== SPEC1 C5 — grace disabled ====================

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task GraceDisabled_EverySupersededTokenGoesStraightToReuseHandling(int graceMinutes)
        {
            CacheMiss();
            Persisted(
                Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: DateTime.UtcNow.AddSeconds(-1), supersededBy: "B"),
                Token("B"));
            _repo.Setup(r => r.RevokeAllByRefreshTokenSessionIdAsync("lineage-1", "token_reuse_detected")).ReturnsAsync(2);
            _cache.Setup(c => c.RemoveKeyAsync("A")).ReturnsAsync(true);

            (await Sut().TryResolveRefreshSessionAsync("A", Config(graceMinutes: graceMinutes))).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync("lineage-1", "token_reuse_detected"), Times.Once);
        }

        [Fact]
        public async Task NullRevokedAt_IsTreatedAsOutsideTheGraceWindow()
        {
            CacheMiss();
            Persisted(Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: null, supersededBy: "B"));
            _repo.Setup(r => r.RevokeAllByRefreshTokenSessionIdAsync("lineage-1", "token_reuse_detected")).ReturnsAsync(1);
            _cache.Setup(c => c.RemoveKeyAsync("A")).ReturnsAsync(true);

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync("lineage-1", "token_reuse_detected"), Times.Once);
        }

        // ==================== SPEC1 C6 — concurrent replay ====================

        [Fact]
        public async Task TwoConcurrentReplaysInsideGrace_BothGetTheSameSuccessor()
        {
            CacheMiss();
            Persisted(
                Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: DateTime.UtcNow.AddMinutes(-1), supersededBy: "B"),
                Token("B"));
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            var sut = Sut();

            var results = await Task.WhenAll(
                sut.TryResolveRefreshSessionAsync("A", Config()),
                sut.TryResolveRefreshSessionAsync("A", Config()));

            results.Should().OnlyContain(r => r != null && r.RefreshToken == "B");
            _repo.Verify(r => r.CreateAsync(It.IsAny<RefreshTokenModel>()), Times.Never);
        }

        // ==================== SPEC1 C7 — a store outage destroys nothing ====================

        [Fact]
        public async Task StoreUnavailable_FailsTheRequestWithoutRevokingOrDeleting()
        {
            CacheMiss();
            _repo.Setup(r => r.GetByTokenIdAsync("A")).ThrowsAsync(new TimeoutException("mongo down"));

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.RevokeByTokenIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _cache.Verify(c => c.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task StoreUnavailableWhileFollowingTheChain_FailsWithoutRevoking()
        {
            CacheMiss();
            Persisted(Token("A", isRevoked: true, revokeReason: Rotation, revokedAt: DateTime.UtcNow.AddMinutes(-1), supersededBy: "B"));
            _repo.Setup(r => r.GetByTokenIdAsync("B")).ThrowsAsync(new TimeoutException("mongo down"));

            (await Sut().TryResolveRefreshSessionAsync("A", Config())).Should().BeNull();
            _repo.Verify(r => r.RevokeAllByRefreshTokenSessionIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task EmptyTokenId_IsRejectedWithoutAnyLookup()
        {
            (await Sut().TryResolveRefreshSessionAsync("", Config())).Should().BeNull();
            _repo.Verify(r => r.GetByTokenIdAsync(It.IsAny<string>()), Times.Never);
        }
    }
}
