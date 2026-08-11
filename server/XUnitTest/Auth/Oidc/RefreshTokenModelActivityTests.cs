using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;

namespace XUnitTest.Auth.Oidc
{
    /// <summary>
    /// SPEC3 H4: <c>IsActive</c> and <c>IsExpired</c> must agree at the same instant, so a listing can
    /// never advertise a session the refresh endpoint would reject. Also covers the lineage-of-one
    /// reading of a document written before lineage tracking shipped (SPEC2 H7).
    /// </summary>
    public sealed class RefreshTokenModelActivityTests
    {
        private static RefreshTokenModel Token(bool revoked, TimeSpan sliding, TimeSpan absolute, string? lineage = "lineage-1") => new()
        {
            TokenId = "t1",
            UserId = "u1",
            RefreshTokenSessionId = lineage,
            IsRevoked = revoked,
            SlidingExpiry = DateTime.UtcNow.Add(sliding),
            AbsoluteExpiry = DateTime.UtcNow.Add(absolute)
        };

        public static TheoryData<bool, int, int, bool> Matrix() => new()
        {
            // revoked, slidingMinutes, absoluteMinutes, expectedActive
            { false,  30,  8640, true  },   // live
            { true,   30,  8640, false },   // revoked
            { false,  -1,  8640, false },   // past sliding only — this is the case that used to read active
            { false,  30,    -1, false },   // past absolute only
            { false,  -1,    -1, false },   // past both
        };

        [Theory]
        [MemberData(nameof(Matrix))]
        public void IsActive_AgreesWithIsExpiredAcrossTheMatrix(bool revoked, int slidingMinutes, int absoluteMinutes, bool expectedActive)
        {
            var token = Token(revoked, TimeSpan.FromMinutes(slidingMinutes), TimeSpan.FromMinutes(absoluteMinutes));

            token.IsActive(DateTime.UtcNow).Should().Be(expectedActive);
            token.IsActive(DateTime.UtcNow).Should().Be(!token.IsRevoked && !token.IsExpired());
        }

        [Fact]
        public void IdledOutTokenIsNoLongerActive()
        {
            // Unrevoked, six days left on the cap, but idle past the sliding window.
            var token = Token(revoked: false, sliding: TimeSpan.FromMinutes(-1), absolute: TimeSpan.FromDays(6));

            token.IsExpired().Should().BeTrue();
            token.IsActive(DateTime.UtcNow).Should().BeFalse();
        }

        [Fact]
        public void EffectiveLineageFallsBackToTheTokenIdOnPreLineageDocuments()
        {
            Token(false, TimeSpan.FromMinutes(30), TimeSpan.FromDays(6), lineage: null)
                .EffectiveRefreshTokenSessionId.Should().Be("t1");
        }

        [Fact]
        public void EffectiveLineageUsesTheStoredValueWhenPresent()
        {
            Token(false, TimeSpan.FromMinutes(30), TimeSpan.FromDays(6))
                .EffectiveRefreshTokenSessionId.Should().Be("lineage-1");
        }
    }
}
