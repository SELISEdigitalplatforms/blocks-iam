using System.Diagnostics;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth
{
    /// <summary>
    /// The unknown-user login state machine against a real Redis (the Lua script is the thing
    /// under test), plus the dummy BCrypt and the username folding.
    /// </summary>
    public sealed class UnknownUserLoginGuardTests : IDisposable
    {
        private readonly RealRedis _redis = new();
        private readonly string _tenant;
        private readonly IdentityConfiguration _config = new(); // threshold 5, base lock 15 min

        public UnknownUserLoginGuardTests()
        {
            _tenant = _redis.NewTenant();
            BlocksContext.IsTestMode = true;
            SetTenant(_tenant);
        }

        public void Dispose()
        {
            _redis.Dispose();
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetTenant(string tenant) =>
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenant, roles: null, userId: null, impersonated: false,
                isAuthenticated: false, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: null,
                userName: null, phoneNumber: null, displayName: null, oauthToken: null,
                originalTenantId: tenant, impersonationSessionId: null, applicationDomain: "test"));

        private UnknownUserLoginGuard Guard() => new(_redis.CacheClient(), NullLogger<UnknownUserLoginGuard>.Instance);

        private string Key(string username) =>
            IamRedisKeys.UnknownLogin(_tenant, UnknownUserLoginGuard.NormalizeUsername(username));

        [RealRedisFact]
        public async Task Fails_CountUp_LockExactlyAtThreshold_ThenRefuseWithoutBcrypt()
        {
            var guard = Guard();
            for (var i = 1; i < _config.GetNumberOfWrongAttemptsToLockTheAccount; i++)
            {
                (await guard.FailAsync("ghost@x.com", "pw" + i, null, _config)).Should().Be(new UnknownUserLoginAttempt(true, false));
                (await guard.GetStateAsync("ghost@x.com")).Should().Be(new UnknownUserLoginState(i, false));
            }

            (await guard.FailAsync("ghost@x.com", "pw", null, _config)).Should().Be(new UnknownUserLoginAttempt(true, true));
            (await guard.GetStateAsync("ghost@x.com")).Locked.Should().BeTrue();
            (await guard.FailAsync("ghost@x.com", "pw", null, _config)).Reserved.Should().BeFalse();

            var ttl = await _redis.Database.KeyTimeToLiveAsync(Key("ghost@x.com"));
            ttl.Should().BeCloseTo(TimeSpan.FromMinutes(15) + UnknownUserLoginGuard.StateLifetime, TimeSpan.FromMinutes(1));
        }

        [RealRedisFact]
        public async Task AfterLockExpires_OneStrikeRelocks_WithLongerLock_LikeARealAccount()
        {
            var guard = Guard();
            for (var i = 0; i < 5; i++)
            {
                await guard.FailAsync("ghost@x.com", "pw", null, _config);
            }

            // Time passes: the lock and the last failure lie in the past.
            var past = DateTimeOffset.UtcNow.AddMinutes(-20).ToUnixTimeMilliseconds();
            await _redis.Database.HashSetAsync(Key("ghost@x.com"), new[] { new HashEntry("u", past), new HashEntry("f", past) });

            var state = await guard.GetStateAsync("ghost@x.com");
            state.Locked.Should().BeFalse();
            CaptchaGate.IsCaptchaRequired(state.FailedCount).Should().BeTrue("the count stays, like FailedLoginCount");

            (await guard.FailAsync("ghost@x.com", "pw", null, _config)).Should().Be(new UnknownUserLoginAttempt(true, true));
            var lockedUntil = (long)await _redis.Database.HashGetAsync(Key("ghost@x.com"), "u");
            DateTimeOffset.FromUnixTimeMilliseconds(lockedUntil).Should()
                .BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(45), TimeSpan.FromMinutes(1), "second lock is 3x the base, as the account backoff");
        }

        [RealRedisFact]
        public async Task ParallelBurst_OnlyThresholdSlots_RestRefused()
        {
            var guard = Guard();
            var results = await Task.WhenAll(Enumerable.Range(0, 30)
                .Select(i => Task.Run(() => guard.FailAsync("burst@x.com", "pw" + i, null, _config))));

            results.Count(r => r.Reserved).Should().Be(5);
            results.Count(r => r.LockedNow).Should().Be(1);
            results.Count(r => !r.Reserved).Should().Be(25);
        }

        [RealRedisFact]
        public async Task CaseWhitespaceAndFullWidthVariants_ShareOneCounter()
        {
            var guard = Guard();
            await guard.FailAsync("Ghost@X.com", "pw", null, _config);
            await guard.FailAsync("  ghost@x.COM  ", "pw", null, _config);
            await guard.FailAsync("ｇｈｏｓｔ＠ｘ．ｃｏｍ", "pw", null, _config);

            (await guard.GetStateAsync("ghost@x.com")).FailedCount.Should().Be(3);
        }

        [RealRedisFact]
        public async Task Tenants_DoNotShareCounters()
        {
            var guard = Guard();
            await guard.FailAsync("ghost@x.com", "pw", null, _config);

            SetTenant(_redis.NewTenant());
            (await guard.GetStateAsync("ghost@x.com")).FailedCount.Should().Be(0);
        }

        [Fact]
        public async Task RedisDown_FailsOpen_StillRunsBcrypt()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
            var guard = new UnknownUserLoginGuard(cache.Object, NullLogger<UnknownUserLoginGuard>.Instance);

            (await guard.GetStateAsync("ghost@x.com")).Should().Be(new UnknownUserLoginState(0, false));

            var sw = Stopwatch.StartNew();
            (await guard.FailAsync("ghost@x.com", "pw", null, _config)).Should().Be(new UnknownUserLoginAttempt(true, false));
            sw.Elapsed.Should().BeGreaterThan(MinimumBcryptTime());
        }

        [Fact]
        public void DummyCheck_CostsAboutWhatARealCheckCosts_AndNeverMatches()
        {
            var realHash = BCrypt.Net.BCrypt.HashPassword("x"); // default work factor, as stored passwords
            UnknownUserLoginGuard.VerifyAgainstDummy("warm-up", null);

            var real = Stopwatch.StartNew();
            BCrypt.Net.BCrypt.Verify("y", realHash);
            real.Stop();

            var dummy = Stopwatch.StartNew();
            UnknownUserLoginGuard.VerifyAgainstDummy("y", "salt").Should().BeFalse();
            dummy.Stop();

            // Generous bound: timing on a busy test host is noisy, but a skipped BCrypt is ~0 ms.
            dummy.Elapsed.Should().BeGreaterThan(real.Elapsed / 3);
        }

        [Fact]
        public void DummyCheck_BlankPassword_IsSkipped_LikeTheRealCheck()
        {
            var sw = Stopwatch.StartNew();
            UnknownUserLoginGuard.VerifyAgainstDummy("  ", null).Should().BeFalse();
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(20));
        }

        [Theory]
        [InlineData("User@Example.com", "user@example.com")]
        [InlineData("  user@example.com\t", "user@example.com")]
        [InlineData("ＵＳＥＲ＠ｅｘａｍｐｌｅ．ｃｏｍ", "user@example.com")]
        [InlineData("Kelvin", "kelvin")] // Kelvin sign
        [InlineData(null, "")]
        public void NormalizeUsername_FoldsCaseWhitespaceAndCompatibilityForms(string? input, string expected) =>
            UnknownUserLoginGuard.NormalizeUsername(input).Should().Be(expected);

        /// <summary>A third of one real BCrypt verify on this host.</summary>
        private static TimeSpan MinimumBcryptTime()
        {
            var hash = BCrypt.Net.BCrypt.HashPassword("x");
            var sw = Stopwatch.StartNew();
            BCrypt.Net.BCrypt.Verify("y", hash);
            return sw.Elapsed / 3;
        }
    }
}
