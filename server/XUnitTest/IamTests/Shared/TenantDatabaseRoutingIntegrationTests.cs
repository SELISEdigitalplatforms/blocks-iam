using System.Diagnostics;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Security.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Mfa.DomainService.Services;
using Mfa.DomainService.TOTP;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.IamTests.Shared;

// Runs against local MongoDB, with unique disposable databases and separate connection
// configurations. Actual independent-cluster availability is a deployment acceptance check.
public class TenantDatabaseRoutingIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedRepositories_IsolateUsersMfaAndSessions_AndKeepBlacklistInRoot(bool concurrent)
    {
        var prefix = "iam_routing_" + Guid.NewGuid().ToString("N");
        var placements = new[] { "main", "dev", "other" };
        var client = new MongoClient("mongodb://localhost:27017");
        var secret = new BlocksSecret { DatabaseConnectionString = "mongodb://localhost:27017", RootDatabaseName = prefix + "_root" };
        var tenants = new Mock<ITenants>(MockBehavior.Strict);
        foreach (var placement in placements)
        {
            var tenantId = placement;
            tenants.Setup(t => t.GetTenantDatabaseConnectionString(tenantId))
                .Returns((prefix + "_" + placement, "mongodb://localhost:27017/?appName=routing-" + placement));
        }
        using var activity = new ActivitySource(nameof(TenantDatabaseRoutingIntegrationTests));
        var provider = new MongoDbContextProvider(NullLogger<MongoDbContextProvider>.Instance, tenants.Object, activity);
        var identity = new IdentityAccessManagementRepository(provider, secret, NullLogger<IdentityAccessManagementRepository>.Instance);
        var users = new UserRepository(identity);
        var authentication = new AuthenticationRepository(provider,
            new OidcDiscoveryClient(Mock.Of<IHttpClientFactory>()), Mock.Of<IKeyValueStore>());
        var mfa = new MfaManagementRepository(provider);
        var refresh = new RefreshTokenRepository(provider, Mock.Of<ICacheClient>(), NullLogger<RefreshTokenRepository>.Instance);
        var sessions = new IdpSessionRepository(provider, NullLogger<IdpSessionRepository>.Instance);
        var security = new SecurityRepository(provider);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await client.GetDatabase(secret.RootDatabaseName).GetCollection<BlackListInformation>("BlackListInformations")
                .InsertOneAsync(new BlackListInformation { Key = "password", Value = "blocked-password" });

            async Task Exercise(string tenantId)
            {
                BlocksContext.SetContext(BlocksContext.Create(tenantId, [], "actor", true, "https://test", "default",
                    DateTime.UtcNow.AddHours(1), "actor@example.com", [], "actor", "", "Actor", "", tenantId));
                try
                {
                    if (concurrent) await gate.Task;
                    // Identical IDs across tenants expose accidental use of a previously captured DB.
                    await users.CreateUserAsync(new User { ItemId = "same-user", Email = tenantId + "@example.com", UserName = tenantId });
                    await mfa.SaveAsync(new Organization { ItemId = "same-org", Name = tenantId });
                    await mfa.SaveAsync(new Role { ItemId = "same-role", Name = tenantId });
                    await mfa.SaveAsync(new Permission { ItemId = "same-permission", Resource = tenantId + ".read" });
                    await mfa.SaveAsync(new UserTotpDetail { ItemId = "same-mfa", Secret = "test-only-" + tenantId });
                    await refresh.CreateAsync(new RefreshTokenModel
                    {
                        TokenId = "same-token", UserId = tenantId, TenantId = tenantId, SessionId = "same-session", ClientId = tenantId
                    });
                    await sessions.CreateAsync(new IdpSessionModel { SessionId = "same-session" });

                    Assert.Equal(tenantId, (await authentication.GetUserByEmailAsync(tenantId + "@example.com")).UserName);
                    Assert.Equal(tenantId, (await identity.GetUserByIdAsync("same-user")).UserName);
                    Assert.Equal(tenantId, (await mfa.GetItemAsync<Organization>(o => o.ItemId == "same-org")).Name);
                    Assert.Equal(tenantId, (await mfa.GetItemAsync<Role>(r => r.ItemId == "same-role")).Name);
                    Assert.Equal(tenantId + ".read", (await mfa.GetItemAsync<Permission>(p => p.ItemId == "same-permission")).Resource);
                    Assert.Equal("test-only-" + tenantId, (await mfa.GetItemAsync<UserTotpDetail>(m => m.ItemId == "same-mfa")).Secret);
                    Assert.Equal(tenantId, (await refresh.GetByTokenIdAsync("same-token")).UserId);
                    Assert.Equal("same-session", (await sessions.GetBySessionIdAsync("same-session")).SessionId);
                    Assert.Equal(tenantId, Assert.Single(await security.GetRotationHistoryAsync("same-session", default)).ClientId);
                    Assert.True(await identity.CheckPasswordBlackListedAsync("blocked-password"));
                }
                finally { BlocksContext.ClearContext(); }
            }

            if (concurrent)
            {
                var tasks = placements.Select(Exercise).ToArray();
                gate.SetResult(true);
                await Task.WhenAll(tasks);
            }
            else
            {
                foreach (var placement in placements) await Exercise(placement);
            }

            foreach (var placement in placements)
            {
                var database = client.GetDatabase(prefix + "_" + placement);
                var collection = database.GetCollection<User>("Users");
                Assert.Equal(1, await collection.CountDocumentsAsync(FilterDefinition<User>.Empty));
                Assert.Equal(0, await collection.CountDocumentsAsync(u => u.UserName != placement));
                Assert.Equal(0, await database.GetCollection<BlackListInformation>("BlackListInformations")
                    .CountDocumentsAsync(FilterDefinition<BlackListInformation>.Empty));
            }
            Assert.Equal(0, await client.GetDatabase(secret.RootDatabaseName).GetCollection<User>("Users")
                .CountDocumentsAsync(FilterDefinition<User>.Empty));
        }
        finally
        {
            foreach (var name in placements.Select(p => prefix + "_" + p).Append(secret.RootDatabaseName))
                await client.DropDatabaseAsync(name);
        }
    }
}
