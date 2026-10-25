using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.DataAccess;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Services;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.DataAccess;

/// <summary>
/// Login, token, refresh and session flows run before any user exists. They must go through the
/// no-user skip path: the real <see cref="IamDataAccess"/> is used here with a STRICT resolver mock
/// (no setup), so any resolver call fails the test.
/// </summary>
public sealed class PreLoginSkipsResolverTests : IDisposable
{
    private readonly Mock<IDataAccessResolver> _resolver = new(MockBehavior.Strict);
    private readonly IamDataAccess _access;
    private readonly Mock<IDbContextProvider> _db = new();

    public PreLoginSkipsResolverTests()
    {
        BlocksContext.IsTestMode = true;
        BlocksContext.SetContext(null); // no user: the state of every pre-login request
        _access = new IamDataAccess(_resolver.Object, NullLogger<IamDataAccess>.Instance);
    }

    public void Dispose()
    {
        BlocksContext.SetContext(null);
        BlocksContext.IsTestMode = false;
    }

    private Mock<IMongoCollection<T>> OnDatabase<T>(string name, IEnumerable<T>? items = null)
    {
        var col = MongoMock.Collection(items);
        var database = MongoMock.Database();
        MongoMock.OnDatabase(database, name, col);
        _db.Setup(d => d.GetDatabase()).Returns(database.Object);
        return col;
    }

    private Mock<IMongoCollection<T>> OnProvider<T>(IEnumerable<T>? items = null)
    {
        var col = MongoMock.Collection(items);
        _db.Setup(d => d.GetCollection<T>(It.IsAny<string>())).Returns(col.Object);
        _db.Setup(d => d.GetCollection<T>(It.IsAny<string>(), It.IsAny<string>())).Returns(col.Object);
        return col;
    }

    [Fact]
    public async Task Login_UserLookupAndLoginRecord_DoNotCallResolver()
    {
        OnProvider(new[] { new User { ItemId = "u1", Email = "a@x.com" } });
        var secret = new Mock<IBlocksSecret>();
        var repo = new IdentityAccessManagementRepository(_db.Object, secret.Object, NullLogger<IdentityAccessManagementRepository>.Instance, _access);

        (await repo.GetUserByEmailAsync("a@x.com")).ItemId.Should().Be("u1");
        (await repo.GetUserByIdAsync("u1")).ItemId.Should().Be("u1");
        await repo.UpdateUserAsync(new User { ItemId = "u1", Email = "a@x.com" });

        _resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Login_AuthenticationRepositoryLookups_DoNotCallResolver()
    {
        OnProvider(new[] { new User { ItemId = "u1", Email = "a@x.com", UserName = "a" } });
        var repo = new AuthenticationRepository(_db.Object,
            new OidcDiscoveryClient(Mock.Of<IHttpClientFactory>()), Mock.Of<IKeyValueStore>(), _access);

        (await repo.GetUserByEmailAsync("a@x.com")).ItemId.Should().Be("u1");
        (await repo.GetUserByUsernameAsync("a")).ItemId.Should().Be("u1");
        (await repo.GetUserByIdAsync("u1")).ItemId.Should().Be("u1");
        await repo.UpdatePartialAsync<User>("u1", new Dictionary<string, object> { ["LogInCount"] = 1 });

        _resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Token_RefreshTokenIssueLookupAndRevoke_DoNotCallResolver()
    {
        OnDatabase("IdpRefreshTokens", new[] { new RefreshTokenModel { TokenId = "t1", UserId = "u1", TenantId = "t", SessionId = "s" } });
        var repo = new RefreshTokenRepository(_db.Object, Mock.Of<ICacheClient>(), NullLogger<RefreshTokenRepository>.Instance, _access);

        await repo.CreateAsync(new RefreshTokenModel { TokenId = "t2", UserId = "u1", TenantId = "t", SessionId = "s" });
        (await repo.GetByTokenIdAsync("t1")).Should().NotBeNull();
        await repo.RevokeByTokenIdAsync("t1", "rotated");

        _resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refresh_RevocationCheckAndSessionLookup_DoNotCallResolver()
    {
        OnDatabase("IdpRevokedTokens", Array.Empty<TokenRevocationModel>());
        var revocations = new TokenRevocationRepository(_db.Object, NullLogger<TokenRevocationRepository>.Instance, _access);
        (await revocations.IsRevokedAsync("jti")).Should().BeFalse();

        var database = MongoMock.Database();
        MongoMock.OnDatabase(database, "IdpSessions", MongoMock.Collection(new[] { new IdpSessionModel { SessionId = "s1" } }));
        _db.Setup(d => d.GetDatabase()).Returns(database.Object);
        var sessions = new IdpSessionRepository(_db.Object, NullLogger<IdpSessionRepository>.Instance, _access);
        (await sessions.GetBySessionIdAsync("s1")).Should().NotBeNull();

        _resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Device_FlowLookups_DoNotCallResolver()
    {
        OnDatabase("DeviceAuthorizationRequests", new[] { new DeviceAuthorizationRequestModel { Id = "d1", DeviceCodeHash = "h" } });
        var repo = new DeviceAuthorizationRepository(_db.Object, NullLogger<DeviceAuthorizationRepository>.Instance, _access);

        (await repo.GetByDeviceCodeHashAsync("h")).Should().NotBeNull();
        await repo.MarkConsumedAsync("d1", DateTime.UtcNow);

        _resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeviceCleanupWorker_OtherTenantSweep_DoesNotCallResolver()
    {
        // The worker passes a tenant id and has no BlocksContext: both reasons point to the skip path.
        var database = MongoMock.Database();
        MongoMock.OnDatabase(database, "DeviceAuthorizationRequests", MongoMock.Collection(Array.Empty<DeviceAuthorizationRequestModel>()));
        _db.Setup(d => d.GetDatabase("tenant-9")).Returns(database.Object);
        var repo = new DeviceAuthorizationRepository(_db.Object, NullLogger<DeviceAuthorizationRepository>.Instance, _access);

        await repo.MarkExpiredAsync("tenant-9", new[] { "d1" });
        await repo.GetExpiredIdsAsync("tenant-9", DateTime.UtcNow, 10);

        _resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticatedCaller_DeniedRead_ReturnsNotFound_WithoutQueryingCollection()
    {
        BlocksContext.SetContext(BlocksContext.Create(
            "t1", new[] { "role" }, "user-1", true, "uri", "org", DateTime.UtcNow.AddHours(1), "e@x.com",
            new[] { "perm" }, "octo", "phone", "display", "oauth", "t1"));
        var col = OnDatabase("IdpRefreshTokens", new[] { new RefreshTokenModel { TokenId = "t1" } });
        _resolver.Setup(r => r.ResolveReadAsync("RefreshTokenModel", It.IsAny<MongoDB.Bson.BsonDocument>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IamReflect.Read(new Blocks.Genesis.DataAccess.Evaluation.PolicyEvaluationResult { IsAccessGranted = false }, null, null));
        var repo = new RefreshTokenRepository(_db.Object, Mock.Of<ICacheClient>(), NullLogger<RefreshTokenRepository>.Instance, _access);

        (await repo.GetByTokenIdAsync("t1")).Should().BeNull();

        col.Verify(c => c.FindAsync(It.IsAny<FilterDefinition<RefreshTokenModel>>(), It.IsAny<FindOptions<RefreshTokenModel, RefreshTokenModel>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
