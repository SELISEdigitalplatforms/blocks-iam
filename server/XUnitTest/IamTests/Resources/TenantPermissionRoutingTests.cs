using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Resources;

public partial class TenantPermissionPropagatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("dev-caller")]
    [InlineData("stg-caller")]
    public async Task GetTargetsAsync_AlwaysDiscoversFromMain_RegardlessOfAmbientContext(string? caller)
    {
        SetContext(caller);
        var tenant = MakeTenant("target", "Target", false, false, "mongodb://dev", "same-db");
        _rootDb.Setup(d => d.GetCollection<Tenant>("Tenants", null)).Returns(MongoMock.Collection(new[] { tenant }).Object);

        var targets = await CreateSut().GetTargetsAsync(caller);

        Assert.Equal("mongodb://dev", Assert.Single(targets).DbConnectionString);
        _dbContextProvider.Verify(p => p.GetDatabase(_secret.DatabaseConnectionString, _secret.RootDatabaseName, false), Times.Once);
        _dbContextProvider.Verify(p => p.GetCollection<Tenant>(It.IsAny<string>()), Times.Never);
        _dbContextProvider.Verify(p => p.GetDatabase(), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PropagateAsync_UsesEveryStoredPlacement_AndReportsIndividualFailures(bool devUnavailable)
    {
        _resourceRepo.Setup(r => r.GetPermissionByIdAsync("perm-1"))
            .ReturnsAsync(new Permission { ItemId = "perm-1", Resource = "shared.read", IsBuiltIn = true });
        var placements = new[] { _secret.DatabaseConnectionString, "mongodb://dev", "mongodb://other" };
        var targets = placements.Select((connection, index) => MakeTenant($"target-{index}", "Target", false, false, connection, "same-db")).ToList();
        // Even a stale root document must never redirect shared templates to another cluster.
        targets.Add(MakeTenant("root", "Root", true, false, "mongodb://wrong-root", "wrong-db"));
        _rootDb.Setup(d => d.GetCollection<Tenant>("Tenants", null)).Returns(MongoMock.Collection(targets).Object);
        var collections = new Dictionary<string, Mock<IMongoCollection<BsonDocument>>>();
        foreach (var target in targets)
        {
            var connection = target.IsRootTenant ? _secret.DatabaseConnectionString : target.DbConnectionString;
            var name = target.IsRootTenant ? "BlocksConfiguration" : target.DBName;
            var database = MongoMock.Database();
            var permissions = MongoMock.Collection<BsonDocument>();
            MongoMock.OnDatabase(database, "Permissions", permissions);
            collections.Add(target.TenantId, permissions);
            _dbContextProvider.Setup(p => p.GetDatabase(connection, name, false)).Returns(database.Object);
        }
        if (devUnavailable)
            _dbContextProvider.Setup(p => p.GetDatabase("mongodb://dev", "same-db", false)).Throws(new TimeoutException("Unavailable"));

        var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Update));

        Assert.Equal(4, summary.TenantsAttempted);
        Assert.Equal(devUnavailable ? 3 : 4, summary.TenantsSucceeded);
        Assert.Equal(devUnavailable ? 1 : 0, summary.TenantsFailed);
        foreach (var target in targets)
        {
            var failed = devUnavailable && target.TenantId == "target-1";
            Assert.Equal(!failed, summary.Results.Single(r => r.TenantId == target.TenantId).Success);
            collections[target.TenantId].Verify(c => c.UpdateManyAsync(It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<UpdateDefinition<BsonDocument>>(), It.IsAny<UpdateOptions>(), default), failed ? Times.Never() : Times.Once());
        }
        _dbContextProvider.Verify(p => p.GetDatabase("mongodb://wrong-root", It.IsAny<string>(), false), Times.Never);
    }
}
