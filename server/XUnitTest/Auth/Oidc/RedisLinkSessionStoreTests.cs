using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using FluentAssertions;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using StackExchange.Redis;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Oidc;

/// <summary>
/// Signup-link sessions in Redis (<see cref="RedisLinkSessionStore"/>) with the MongoDB fallback of
/// older pods (<see cref="LinkSessionRepository"/>). Real Redis / Mongo tests use the local throwaway
/// test containers and skip when none answers.
/// </summary>
public sealed class RedisLinkSessionStoreTests : IDisposable
{
    private readonly RealRedis _redis = new();

    public void Dispose() => _redis.Dispose();

    private RedisLinkSessionStore Store(IDbContextProvider? mongo = null, ICacheClient? cache = null) =>
        new(cache ?? _redis.CacheClient(),
            new LinkSessionRepository(mongo ?? EmptyMongo()),
            NullLogger<RedisLinkSessionStore>.Instance);

    private static IDbContextProvider EmptyMongo()
    {
        var db = new Mock<IDbContextProvider>();
        var database = MongoMock.Database();
        var col = MongoMock.Collection<LinkSessionModel>(null);
        MongoMock.SetupWrites(col);
        MongoMock.OnDatabase(database, LinkSessionRepository.CollectionName, col);
        db.Setup(d => d.GetDatabase()).Returns(database.Object);
        return db.Object;
    }

    private static LinkSessionModel Session(string tenant) => new()
    {
        UserId = "user-1",
        TenantId = tenant,
        OrganizationId = "org-1",
        ClientId = "client-1",
        LinkId = "link-1",
        Roles = new() { "r1" },
        Permissions = new() { "p1" },
        Amr = new() { "link" },
        IssuedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
    };

    [RealRedisFact]
    [Trait("Category", "RedisIntegration")]
    public async Task Create_StoresValueAndTtlAtMost1800_UnderTheHashedKey()
    {
        var tenant = _redis.NewTenant();
        var session = Session(tenant);

        await Store().CreateAsync(session);

        var key = IamRedisKeys.LinkSession(tenant, session.SessionId);
        key.Should().NotContain(session.SessionId);
        var ttl = await _redis.Database.KeyTimeToLiveAsync(key);
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(1700)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(1800));
        (await _redis.Database.KeyTypeAsync(key)).Should().Be(RedisType.String);
        var stored = (string)(await _redis.Database.StringGetAsync(key))!;
        stored.Should().NotContain(session.SessionId);
        System.Text.Json.JsonDocument.Parse(stored).RootElement.TryGetProperty("SessionId", out _).Should().BeFalse();
    }

    [RealRedisFact]
    [Trait("Category", "RedisIntegration")]
    public async Task Read_ManyTimes_ThenDelete_KeyGone()
    {
        var tenant = _redis.NewTenant();
        var session = Session(tenant);
        var store = Store();
        await store.CreateAsync(session);

        for (var i = 0; i < 3; i++)
        {
            var read = await store.GetBySessionIdAsync(tenant, session.SessionId);
            read.Should().BeEquivalentTo(session, o => o
                .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromMilliseconds(1))).WhenTypeIs<DateTime>());
        }

        await store.DeleteAsync(tenant, session.SessionId);

        (await _redis.Database.KeyExistsAsync(IamRedisKeys.LinkSession(tenant, session.SessionId))).Should().BeFalse();
        (await store.GetBySessionIdAsync(tenant, session.SessionId)).Should().BeNull();
    }

    [RealRedisFact]
    [Trait("Category", "RedisIntegration")]
    public async Task Read_UnderAnotherTenant_IsNotFound()
    {
        var tenant = _redis.NewTenant();
        var session = Session(tenant);
        var store = Store();
        await store.CreateAsync(session);

        (await store.GetBySessionIdAsync(_redis.NewTenant(), session.SessionId)).Should().BeNull();
    }

    [RealRedisFact]
    [Trait("Category", "RedisIntegration")]
    public async Task ExpiredKey_IsMissing()
    {
        var tenant = _redis.NewTenant();
        var session = Session(tenant);
        var store = Store();
        await store.CreateAsync(session);
        await _redis.Database.KeyExpireAsync(IamRedisKeys.LinkSession(tenant, session.SessionId), TimeSpan.FromMilliseconds(50));
        await Task.Delay(200);

        (await store.GetBySessionIdAsync(tenant, session.SessionId)).Should().BeNull();
    }

    [RealRedisAndMongoFact]
    [Trait("Category", "RedisIntegration")]
    public async Task OldPodsSession_IsReadFromMongo_AndDeleteRemovesTheMongoRowToo()
    {
        using var mongo = new RealMongo();
        var tenant = _redis.NewTenant();
        var legacy = Session(tenant);
        var col = mongo.Database.GetCollection<LinkSessionModel>(LinkSessionRepository.CollectionName);
        await col.InsertOneAsync(legacy);
        var store = Store(mongo.ProviderFor());

        (await store.GetBySessionIdAsync(tenant, legacy.SessionId))!.UserId.Should().Be("user-1");
        await store.DeleteAsync(tenant, legacy.SessionId);

        (await col.CountDocumentsAsync(FilterDefinition<LinkSessionModel>.Empty)).Should().Be(0);
        (await store.GetBySessionIdAsync(tenant, legacy.SessionId)).Should().BeNull();
    }

    [RealRedisAndMongoFact]
    [Trait("Category", "RedisIntegration")]
    public async Task NewSessions_NeverWriteMongo_AndADeleteNeverCreatesTheDroppedCollection()
    {
        using var mongo = new RealMongo();
        var tenant = _redis.NewTenant();
        var store = Store(mongo.ProviderFor());
        var session = Session(tenant);

        await store.CreateAsync(session);
        await store.DeleteAsync(tenant, session.SessionId);

        var names = await (await mongo.Database.ListCollectionNamesAsync()).ToListAsync();
        names.Should().NotContain(LinkSessionRepository.CollectionName);
    }

    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task RedisDown_FailsTheRequest_AndNeverReadsMongo()
    {
        var mongo = new Mock<IDbContextProvider>(MockBehavior.Strict);
        var store = Store(mongo.Object, RealRedis.CacheClientFor(RealRedis.Unreachable()));

        await FluentActions.Invoking(() => store.GetBySessionIdAsync("T1", "sid")).Should().ThrowAsync<RedisException>();
        await FluentActions.Invoking(() => store.CreateAsync(Session("T1"))).Should().ThrowAsync<RedisException>();
        mongo.Verify(m => m.GetDatabase(), Times.Never);
    }

    [Fact]
    public async Task BlankSessionId_ReadsNothing_DeletesNothing()
    {
        var cache = new Mock<ICacheClient>(MockBehavior.Strict);
        var store = Store(new Mock<IDbContextProvider>(MockBehavior.Strict).Object, cache.Object);

        (await store.GetBySessionIdAsync("T1", "")).Should().BeNull();
        await store.DeleteAsync("T1", " ");
    }
}
