using System.Collections.Concurrent;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Rotation claims its predecessor atomically, and reuse handling revokes and evicts the whole lineage.
    /// Runs on the local throwaway test Mongo (skipped when none answers); the cache is in memory.
    /// </summary>
    public sealed class RefreshRotationClaimTests : IDisposable
    {
        private const string Collection = "IdpRefreshTokens";
        private const string Lineage = "lineage-1";

        private readonly RealMongo? _mongo = RealMongo.IsAvailable ? new RealMongo() : null;
        private readonly ConcurrentDictionary<string, string> _cacheStore = new();
        private readonly ICacheClient _cache;

        public RefreshRotationClaimTests()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string k) => _cacheStore.TryGetValue(k, out var v) ? v : null!);
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync((string k, string v, long _) => { _cacheStore[k] = v; return true; });
            cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>()))
                .ReturnsAsync((string k) => _cacheStore.TryRemove(k, out _));
            _cache = cache.Object;
        }

        public void Dispose() => _mongo?.Dispose();

        private IMongoCollection<RefreshTokenModel> Tokens => _mongo!.Database.GetCollection<RefreshTokenModel>(Collection);

        private RefreshTokenRepository Repository() =>
            new(_mongo!.ProviderFor(), _cache, NullLogger<RefreshTokenRepository>.Instance, new XUnitTest.TestDoubles.PassThroughIamDataAccess());

        private RefreshSessionResolver Resolver(RefreshTokenRepository repository) =>
            new(_cache, repository, NullLogger<RefreshSessionResolver>.Instance);

        private UnifiedTokenSessionService Service(RefreshTokenRepository repository)
        {
            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(a => a.GetDeviceInfo(It.IsAny<string>())).Returns(new DeviceInformation { Device = "Test" });
            var idpSession = new Mock<IIdpSessionService>();
            idpSession.Setup(s => s.ResolveOrCreateAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync("session-1");

            return new UnifiedTokenSessionService(
                _cache, authDomain.Object, repository, new Mock<IUserActivityDispatcher>().Object, idpSession.Object,
                new Mock<IHttpContextAccessor>().Object, new Mock<IUserRepository>().Object,
                NullLogger<UnifiedTokenSessionService>.Instance, refreshSessionResolver: Resolver(repository));
        }

        private static IdentityConfiguration Config(int graceMinutes) => new()
        {
            RefreshTokenValidForNumberMinutes = 60,
            AbsoluteRefreshTokenValidForNumberMinutes = 10080,
            TokenRotationGracePeriodMinutes = graceMinutes
        };

        private static RefreshTokenModel Stored(string tokenId, string? lineage = Lineage) => new()
        {
            TokenId = tokenId,
            UserId = "user-1",
            TenantId = "tenant-1",
            ClientId = "client-1",
            SessionId = "session-1",
            RefreshTokenSessionId = lineage,
            IssuedUtc = DateTime.UtcNow.AddMinutes(-10),
            SlidingExpiry = DateTime.UtcNow.AddMinutes(30),
            AbsoluteExpiry = DateTime.UtcNow.AddDays(6),
            IsRevoked = false
        };

        private static RefreshTokenCache Cached(RefreshTokenModel token) => new()
        {
            RefreshToken = token.TokenId,
            UserId = token.UserId,
            TenantId = token.TenantId,
            ClientId = token.ClientId,
            SessionId = token.SessionId ?? string.Empty,
            RefreshTokenSessionId = token.RefreshTokenSessionId,
            IssuedUtc = token.IssuedUtc,
            ExpiresUtc = token.SlidingExpiry,
            AbsoluteExpiresUtc = token.AbsoluteExpiry
        };

        private async Task<RefreshTokenCache> SeedAsync(RefreshTokenModel token)
        {
            await Tokens.InsertOneAsync(token);
            var cached = Cached(token);
            _cacheStore[token.TokenId] = System.Text.Json.JsonSerializer.Serialize(cached);
            return cached;
        }

        private static Task<(string RefreshToken, DateTime SlidingExpiry, DateTime AbsoluteExpiry, string RefreshTokenSessionId)> RotateAsync(
            UnifiedTokenSessionService service, RefreshTokenCache predecessor, IdentityConfiguration config, string grantType = "refresh_token") =>
            service.CreateOrRotateRefreshToken(
                new RefreshTokenPredecessor(predecessor.RefreshToken, predecessor),
                new TokenRequest { ClientId = "client-1", GrantType = grantType, Scope = "openid", OrganizationId = "default", IdpSessionId = "session-1" },
                config,
                XUnitTest.Auth.OAuth.RefreshTokenAuthenticationServiceTests.MakeTenant(),
                new User { ItemId = "user-1", TokenVersion = 1 },
                new[] { "127.0.0.1" },
                impersoanted: false);

        private async Task<List<RefreshTokenModel>> LiveAsync() =>
            await Tokens.Find(t => !t.IsRevoked).ToListAsync();

        [RealMongoFact]
        public async Task Claim_SucceedsOnce_AndReleaseMakesTheTokenLiveAgain()
        {
            await SeedAsync(Stored("t0"));
            var repository = Repository();

            (await repository.TryClaimForRotationAsync("t0", "s1", "superseded_by_rotation")).Should().BeTrue();
            (await repository.TryClaimForRotationAsync("t0", "s2", "superseded_by_rotation")).Should().BeFalse();
            (await repository.ReleaseRotationClaimAsync("t0", "s2")).Should().BeFalse("only the winner's successor releases");

            var claimed = await repository.GetByTokenIdAsync("t0");
            claimed.SupersededByTokenId.Should().Be("s1");
            claimed.RevokeReason.Should().Be("superseded_by_rotation");

            (await repository.ReleaseRotationClaimAsync("t0", "s1")).Should().BeTrue();
            var released = await repository.GetByTokenIdAsync("t0");
            released.IsRevoked.Should().BeFalse();
            released.SupersededByTokenId.Should().BeNull();
        }

        [RealMongoFact]
        public async Task ParallelRotations_InsideGrace_LeaveOneLiveSuccessor_AndBothCallersGetIt()
        {
            var predecessor = await SeedAsync(Stored("t0"));
            var repository = Repository();
            var service = Service(repository);

            var results = await Task.WhenAll(
                RotateAsync(service, predecessor, Config(graceMinutes: 5)),
                RotateAsync(service, predecessor, Config(graceMinutes: 5)));

            var live = await LiveAsync();
            live.Should().ContainSingle();
            results.Select(r => r.RefreshToken).Should().AllBe(live[0].TokenId);
            results.Select(r => r.RefreshTokenSessionId).Should().AllBe(Lineage);
            (await Tokens.CountDocumentsAsync(FilterDefinition<RefreshTokenModel>.Empty)).Should().Be(2, "the loser minted nothing");
            (await repository.GetByTokenIdAsync("t0")).SupersededByTokenId.Should().Be(live[0].TokenId);
        }

        [RealMongoFact]
        public async Task ParallelRotations_WithGraceZero_TreatTheLoserAsReuse_AndEndTheLineage()
        {
            var predecessor = await SeedAsync(Stored("t0"));
            var repository = Repository();
            var service = Service(repository);

            var results = await Task.WhenAll(
                RotateAsync(service, predecessor, Config(graceMinutes: 0)),
                RotateAsync(service, predecessor, Config(graceMinutes: 0)));

            results.Count(r => string.IsNullOrEmpty(r.RefreshToken)).Should().Be(1);
            (await LiveAsync()).Should().BeEmpty("reuse handling revoked the winner's successor too");
            var winner = await repository.GetByTokenIdAsync(results.Single(r => !string.IsNullOrEmpty(r.RefreshToken)).RefreshToken);
            (await RotateAsync(service, Cached(winner), Config(graceMinutes: 0))).RefreshToken.Should().BeEmpty("a revoked token cannot be claimed, cached or not");
            (await Tokens.CountDocumentsAsync(FilterDefinition<RefreshTokenModel>.Empty)).Should().Be(2);
        }

        [RealMongoFact]
        public async Task ReuseDetection_RefusesTheNewestLineageToken_EvenThoughItWasCached()
        {
            var old = Stored("t0");
            old.IsRevoked = true;
            old.RevokeReason = "superseded_by_rotation";
            old.RevokedAt = DateTime.UtcNow.AddMinutes(-10);
            old.SupersededByTokenId = "t1";
            await Tokens.InsertOneAsync(old);
            await SeedAsync(Stored("t1"));
            var resolver = Resolver(Repository());

            (await resolver.TryResolveRefreshSessionAsync("t0", Config(graceMinutes: 5))).Should().BeNull();

            _cacheStore.Should().NotContainKey("t1");
            (await resolver.TryResolveRefreshSessionAsync("t1", Config(graceMinutes: 5))).Should().BeNull();
            (await LiveAsync()).Should().BeEmpty();
        }

        [RealMongoFact]
        public async Task LostClaim_OnAContextChangingGrant_ReturnsNothing_AndMintsNothing()
        {
            var predecessor = await SeedAsync(Stored("t0"));
            var repository = Repository();
            await repository.TryClaimForRotationAsync("t0", "winner", "superseded_by_rotation");
            await Tokens.InsertOneAsync(Stored("winner"));

            var result = await RotateAsync(Service(repository), predecessor, Config(graceMinutes: 5), grantType: "switch_organization");

            result.RefreshToken.Should().BeEmpty();
            (await Tokens.CountDocumentsAsync(FilterDefinition<RefreshTokenModel>.Empty)).Should().Be(2);
        }

        [RealMongoFact]
        public async Task PreLineageToken_RotatesOnce_AndStartsItsLineageFromItsOwnId()
        {
            var predecessor = await SeedAsync(Stored("legacy", lineage: null));

            var result = await RotateAsync(Service(Repository()), predecessor, Config(graceMinutes: 5));

            result.RefreshToken.Should().NotBeNullOrEmpty();
            result.RefreshTokenSessionId.Should().Be("legacy");
            var live = await LiveAsync();
            live.Should().ContainSingle().Which.TokenId.Should().Be(result.RefreshToken);
        }
    }
}
