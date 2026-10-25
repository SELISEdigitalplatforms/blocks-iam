using System.Text.Json;
using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Oidc
{
    /// <summary>
    /// Authorization codes in Redis (<see cref="RedisAuthorizationCodeStore"/>) with the read-only
    /// MongoDB fallback (<see cref="AuthorizationCodeRepository"/>). Atomic single use can only be proven
    /// on a real Redis, so those tests run against the local throwaway test Redis
    /// (<c>Category=RedisIntegration</c>) and skip when none answers.
    /// </summary>
    public sealed class AuthorizationCodeRepositoryTests : IDisposable
    {
        private const string MongoCollection = "IdpAuthorizationCodes";

        private readonly RealRedis _redis = new();
        private readonly string _tenant;

        public AuthorizationCodeRepositoryTests()
        {
            _tenant = _redis.NewTenant();
            UseTenant(_tenant);
        }

        public void Dispose()
        {
            _redis.Dispose();
            BlocksContext.SetContext(null);
        }

        private static void UseTenant(string? tenantId)
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(tenantId == null ? null : BlocksContext.Create(
                tenantId: tenantId, roles: null, userId: null, impersonated: false,
                isAuthenticated: false, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: null,
                userName: null, phoneNumber: null, displayName: null, oauthToken: null,
                originalTenantId: tenantId, impersonationSessionId: null, applicationDomain: "test"));
        }

        private static AuthorizationCodeModel NewCode(string? code = null) => new()
        {
            Code = code ?? "raw-code-" + Guid.NewGuid().ToString("N"),
            ClientId = "client-1",
            TenantId = "tenant-claim",
            UserId = "user-1",
            RedirectUri = "https://app/cb",
            Scope = "openid",
            CodeChallenge = "challenge",
            CodeChallengeMethod = "S256",
            Amr = new() { "pwd" },
            IdpSessionId = "idp-1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow,
            Impersonated = true,
            TargetedTenantId = "target-tenant",
            ImpersonatedUserId = "imp-user"
        };

        private RedisAuthorizationCodeStore Store(IDbContextProvider? mongo = null, ICacheClient? cache = null) =>
            new(cache ?? _redis.CacheClient(),
                new AuthorizationCodeRepository(mongo ?? new Mock<IDbContextProvider>(MockBehavior.Strict).Object),
                NullLogger<RedisAuthorizationCodeStore>.Instance);

        private string Key(string code) => IamRedisKeys.AuthorizationCode(_tenant, code);

        private static AuthorizationCodeMint Mint(string lineage = "lineage-1") => new()
        {
            LineageId = lineage, Jti = "jti-1", UserId = "user-1", AccessExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        // ---------------- unit: keys ----------------

        [Fact]
        public void Keys_HashTheSecretPart_NeverRaw_AndCarryThePlainTenant()
        {
            var key = IamRedisKeys.AuthorizationCode("TENANT1", "my-raw-code");
            var link = IamRedisKeys.LinkSession("TENANT1", "my-session-id");

            key.Should().Be("iam:TENANT1:authcode:" + IamRedisKeys.Sha256Hex("my-raw-code"));
            link.Should().Be("iam:TENANT1:linksession:" + IamRedisKeys.Sha256Hex("my-session-id"));
            key.Should().NotContain("my-raw-code").And.NotContain("{").And.NotContain("}");
            link.Should().NotContain("my-session-id");
            IamRedisKeys.Sha256Hex("my-raw-code").Should().MatchRegex("^[0-9a-f]{64}$");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void Keys_WithoutTenant_Throw(string? tenant)
        {
            FluentActions.Invoking(() => IamRedisKeys.AuthorizationCode(tenant, "c")).Should().Throw<InvalidOperationException>();
            FluentActions.Invoking(() => IamRedisKeys.LinkSession(tenant, "s")).Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public async Task Create_WithoutTenantInContext_Fails_AsToday()
        {
            UseTenant(null);
            var cache = new Mock<ICacheClient>(MockBehavior.Strict);

            var act = () => Store(cache: cache.Object).CreateAsync(NewCode());

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // ---------------- unit: MongoDB fallback (mocked) ----------------

        [Fact]
        public async Task MongoFallback_GetByCode_ReturnsMatch()
        {
            var db = new Mock<IDbContextProvider>();
            var database = MongoMock.Database();
            MongoMock.OnDatabase(database, MongoCollection, MongoMock.Collection(new[] { new AuthorizationCodeModel { Code = "abc", UserId = "u1" } }));
            db.Setup(d => d.GetDatabase()).Returns(database.Object);

            (await new AuthorizationCodeRepository(db.Object).GetByCodeAsync("abc")).UserId.Should().Be("u1");
        }

        [Fact]
        public async Task MongoFallback_NoDatabase_Throws()
        {
            var db = new Mock<IDbContextProvider>();
            db.Setup(d => d.GetDatabase()).Returns((IMongoDatabase?)null);

            var act = () => new AuthorizationCodeRepository(db.Object).GetByCodeAsync("abc");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // ---------------- real Redis ----------------

        [RealRedisFact]
        [Trait("Category", "RedisIntegration")]
        public async Task Create_StoresOnlyTheDataField_WithTtlAtMost660_AndNoRawCode()
        {
            var code = NewCode();

            (await Store().CreateAsync(code)).Should().Be(code.Code);

            var db = _redis.Database;
            var ttl = await db.KeyTimeToLiveAsync(Key(code.Code));
            ttl.Should().NotBeNull();
            ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(600)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(660));
            var fields = await db.HashGetAllAsync(Key(code.Code));
            fields.Select(f => f.Name.ToString()).Should().BeEquivalentTo("data");
            var json = fields[0].Value.ToString();
            json.Should().NotContain(code.Code);
            JsonDocument.Parse(json).RootElement.TryGetProperty("Code", out _).Should().BeFalse();
            JsonDocument.Parse(json).RootElement.TryGetProperty("Source", out _).Should().BeFalse();
        }

        [RealRedisFact]
        [Trait("Category", "RedisIntegration")]
        public async Task Read_ReturnsTheWholeModel_WithCodeAndRedisSource_NoWrite()
        {
            var code = NewCode();
            var store = Store();
            await store.CreateAsync(code);

            var read = await store.GetByCodeAsync(code.Code);

            read.Code.Should().Be(code.Code);
            read.Source.Should().Be(AuthorizationCodeSource.Redis);
            read.Should().BeEquivalentTo(code, o => o.Excluding(c => c.Source)
                .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromMilliseconds(1))).WhenTypeIs<DateTime>());
            (await _redis.Database.HashLengthAsync(Key(code.Code))).Should().Be(1);
        }

        [RealRedisFact]
        [Trait("Category", "RedisIntegration")]
        public async Task TwentyParallelConsumes_ExactlyOneWins_TheRestAreReuse()
        {
            var code = NewCode();
            var store = Store();
            await store.CreateAsync(code);
            var model = await store.GetByCodeAsync(code.Code);

            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.TryConsumeAsync(model))));

            results.Count(r => r == AuthorizationCodeConsumeResult.Consumed).Should().Be(1);
            results.Count(r => r == AuthorizationCodeConsumeResult.Reused).Should().Be(19);
            (await _redis.Database.HashExistsAsync(Key(code.Code), "consumedAt")).Should().BeTrue();
        }

        [RealRedisFact]
        [Trait("Category", "RedisIntegration")]
        public async Task DataIsKeptAfterUse_UntilTheTtlEnds_SoTheImpersonationCallbackStillReadsIt()
        {
            var code = NewCode();
            var store = Store();
            await store.CreateAsync(code);
            var model = await store.GetByCodeAsync(code.Code);
            await store.TryConsumeAsync(model);
            await store.RecordMintAsync(model, Mint());

            var again = await store.GetByCodeAsync(code.Code);

            again.Should().NotBeNull();
            again.Impersonated.Should().BeTrue();
            again.TargetedTenantId.Should().Be("target-tenant");
            again.ImpersonatedUserId.Should().Be("imp-user");
        }

        [RealRedisFact]
        [Trait("Category", "RedisIntegration")]
        public async Task FirstUse_ThenMint_StoresConsumedAtAndMinted_KeepsTheTtl()
        {
            var code = NewCode();
            var store = Store();
            await store.CreateAsync(code);
            var model = await store.GetByCodeAsync(code.Code);

            (await store.TryConsumeAsync(model)).Should().Be(AuthorizationCodeConsumeResult.Consumed);
            (await store.RecordMintAsync(model, Mint())).Should().BeTrue();

            var db = _redis.Database;
            var minted = JsonSerializer.Deserialize<AuthorizationCodeMint>((string)(await db.HashGetAsync(Key(code.Code), "minted"))!);
            minted!.LineageId.Should().Be("lineage-1");
            minted.Jti.Should().Be("jti-1");
            JsonDocument.Parse((string)(await db.HashGetAsync(Key(code.Code), "minted"))!).RootElement
                .EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("lineageId", "jti", "userId", "accessExpiresAt");
            (await db.KeyTimeToLiveAsync(Key(code.Code))).Should().NotBeNull();
        }

        [RealRedisFact]
        [Trait("Category", "RedisIntegration")]
        public async Task Reuse_SetsRevoked_AndReturnsWhatTheFirstUseMinted()
        {
            var code = NewCode();
            var store = Store();
            await store.CreateAsync(code);
            var model = await store.GetByCodeAsync(code.Code);
            await store.TryConsumeAsync(model);
            await store.RecordMintAsync(model, Mint("lineage-x"));

            (await store.TryConsumeAsync(model)).Should().Be(AuthorizationCodeConsumeResult.Reused);
            var minted = await store.MarkReusedAsync(model);

            minted!.LineageId.Should().Be("lineage-x");
            minted.UserId.Should().Be("user-1");
            (await _redis.Database.HashGetAsync(Key(code.Code), "revoked")).ToString().Should().Be("1");
        }

        [RealRedisFact]
        [Trait("Category", "RedisIntegration")]
        public async Task ReuseBeforeTheMint_ReturnsNothing_ThenTheMintIsRefused()
        {
            var code = NewCode();
            var store = Store();
            await store.CreateAsync(code);
            var model = await store.GetByCodeAsync(code.Code);
            await store.TryConsumeAsync(model);

            (await store.MarkReusedAsync(model)).Should().BeNull();
            (await store.RecordMintAsync(model, Mint())).Should().BeFalse();
            (await _redis.Database.HashExistsAsync(Key(code.Code), "minted")).Should().BeFalse();
        }

        [RealRedisFact]
        [Trait("Category", "RedisIntegration")]
        public async Task ConsumeMintReuse_OnAMissingOrExpiredKey_ReturnMinusOne_AndNeverCreateTheKey()
        {
            var code = NewCode();
            var store = Store(new EmptyMongo().Provider);
            await store.CreateAsync(code);
            var model = await store.GetByCodeAsync(code.Code);
            var db = _redis.Database;
            await db.KeyExpireAsync(Key(code.Code), TimeSpan.FromMilliseconds(50));
            await Task.Delay(200);

            (await store.GetByCodeAsync(code.Code)).Should().BeNull("an expired key is missing; the fallback Mongo has nothing");
            (await store.TryConsumeAsync(model)).Should().Be(AuthorizationCodeConsumeResult.Refused);
            (await store.RecordMintAsync(model, Mint())).Should().BeFalse();
            (await store.MarkReusedAsync(model)).Should().BeNull();
            (await db.KeyExistsAsync(Key(code.Code))).Should().BeFalse();

            var never = new AuthorizationCodeModel { Code = "never-created" };
            (await store.TryConsumeAsync(never)).Should().Be(AuthorizationCodeConsumeResult.Refused);
            (await store.RecordMintAsync(never, Mint())).Should().BeFalse();
            (await store.MarkReusedAsync(never)).Should().BeNull();
            (await db.KeyExistsAsync(Key("never-created"))).Should().BeFalse();
        }

        [RealRedisFact]
        [Trait("Category", "RedisIntegration")]
        public async Task ACodeFromTenantA_IsNotFoundForTenantB()
        {
            var code = NewCode();
            await Store().CreateAsync(code);

            UseTenant(_redis.NewTenant());
            var mongo = new EmptyMongo();

            (await Store(mongo.Provider).GetByCodeAsync(code.Code)).Should().BeNull();
        }

        [RealRedisAndMongoFact]
        [Trait("Category", "RedisIntegration")]
        public async Task RedisMiss_FallsBackToMongo_AndConsumesThereOnce_WithTodaysRule()
        {
            using var mongo = new RealMongo();
            var legacy = NewCode();
            await mongo.Database.GetCollection<AuthorizationCodeModel>(MongoCollection).InsertOneAsync(legacy);
            var store = Store(mongo.ProviderFor());

            var read = await store.GetByCodeAsync(legacy.Code);

            read.Source.Should().Be(AuthorizationCodeSource.Mongo);
            (await store.TryConsumeAsync(read)).Should().Be(AuthorizationCodeConsumeResult.Consumed);
            (await store.TryConsumeAsync(read)).Should().Be(AuthorizationCodeConsumeResult.Refused);
            (await store.RecordMintAsync(read, Mint())).Should().BeTrue();
            (await store.MarkReusedAsync(read)).Should().BeNull();
            (await _redis.Database.KeyExistsAsync(Key(legacy.Code))).Should().BeFalse("a Mongo code never writes Redis");
        }

        [RealRedisAndMongoFact]
        [Trait("Category", "RedisIntegration")]
        public async Task ARedisCode_IsNeverConsumedInMongo_EvenWhenMongoHasTheSameCode()
        {
            using var mongo = new RealMongo();
            var code = NewCode();
            var col = mongo.Database.GetCollection<AuthorizationCodeModel>(MongoCollection);
            await col.InsertOneAsync(code);
            var store = Store(mongo.ProviderFor());
            await store.CreateAsync(code);

            var read = await store.GetByCodeAsync(code.Code);
            (await store.TryConsumeAsync(read)).Should().Be(AuthorizationCodeConsumeResult.Consumed);

            read.Source.Should().Be(AuthorizationCodeSource.Redis);
            (await col.Find(c => c.Code == code.Code).FirstAsync()).IsRevoked.Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "RedisIntegration")]
        public async Task RedisDown_FailsTheRequest_AndNeverReadsMongo()
        {
            var mongo = new Mock<IDbContextProvider>(MockBehavior.Strict);
            var store = Store(mongo.Object, RealRedis.CacheClientFor(RealRedis.Unreachable()));
            var model = NewCode();

            await FluentActions.Invoking(() => store.GetByCodeAsync(model.Code)).Should().ThrowAsync<RedisException>();
            await FluentActions.Invoking(() => store.CreateAsync(model)).Should().ThrowAsync<Exception>();
            await FluentActions.Invoking(() => store.TryConsumeAsync(model)).Should().ThrowAsync<RedisException>();
            mongo.Verify(m => m.GetDatabase(), Times.Never);
        }

        /// <summary>A mocked Mongo provider for tests that only need "Mongo has nothing".</summary>
        private sealed class EmptyMongo
        {
            public IDbContextProvider Provider { get; }

            public EmptyMongo()
            {
                var db = new Mock<IDbContextProvider>();
                var database = MongoMock.Database();
                MongoMock.OnDatabase(database, MongoCollection, MongoMock.Collection<AuthorizationCodeModel>(null));
                db.Setup(d => d.GetDatabase()).Returns(database.Object);
                Provider = db.Object;
            }
        }
    }
}
