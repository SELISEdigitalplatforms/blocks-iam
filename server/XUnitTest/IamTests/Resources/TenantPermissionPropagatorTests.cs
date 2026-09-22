using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.TenantPropagation;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    /// <summary>
    /// Unit tests for <see cref="TenantPermissionPropagator"/>. Repository and db-context-provider
    /// dependencies are mocked. Target connections are resolved through the shared provider;
    /// routing tests distinguish main, dev and other even when database names match.
    /// </summary>
    public partial class TenantPermissionPropagatorTests : IDisposable
    {
        private const string SourceTenantId = "tenant-source";

        private readonly Mock<IResourceRepository> _resourceRepo = new();
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IMongoDatabase> _rootDb = new();
        private readonly BlocksSecret _secret = new()
        {
            DatabaseConnectionString = "mongodb://localhost:27017",
            RootDatabaseName = "BlocksRootDb"
        };

        public TenantPermissionPropagatorTests()
        {
            _dbContextProvider.Setup(p => p.GetDatabase(It.IsAny<string>(), It.IsAny<string>(), false))
                .Returns((string connection, string database, bool _) => new MongoClient(connection).GetDatabase(database));
            _dbContextProvider.Setup(p => p.GetDatabase(_secret.DatabaseConnectionString, _secret.RootDatabaseName, false))
                .Returns(_rootDb.Object);
            BlocksContext.IsTestMode = true;
            SetContext(SourceTenantId);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetContext(string? tenantId)
        {
            if (tenantId == null)
            {
                BlocksContext.SetContext(null);
                return;
            }

            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: tenantId, impersonationSessionId: null, applicationDomain: "test"));
        }

        private TenantPermissionPropagator CreateSut() =>
            new(_resourceRepo.Object, _dbContextProvider.Object, NullLogger<TenantPermissionPropagator>.Instance, _secret);

        private static Mock<IMongoCollection<T>> MockCollection<T>(IEnumerable<T> items)
        {
            var list = items.ToList();
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(list);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true).ReturnsAsync(false);

            var collection = new Mock<IMongoCollection<T>>();
            collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<T>>(),
                    It.IsAny<FindOptions<T, T>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);
            return collection;
        }

        private static PermissionMutationForTenantsEvent Event(MutationEventType action = MutationEventType.Create) =>
            new() { ItemId = "perm-1", Action = action };

        private static Tenant MakeTenant(
            string tenantId, string name, bool isRoot, bool isDisabled, string connString, string dbName = "") =>
            new()
            {
                TenantId = tenantId,
                Name = name,
                IsRootTenant = isRoot,
                IsDisabled = isDisabled,
                DbConnectionString = connString,
                DBName = dbName,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
            };

        // ---------- PropagateAsync: early exits ----------

        [Fact]
        public async Task PropagateAsync_PermissionNotFound_SkipsAndReturnsEmptySummary()
        {
            _resourceRepo.Setup(r => r.GetPermissionByIdAsync("perm-1")).ReturnsAsync((Permission)null);

            var summary = await CreateSut().PropagateAsync(Event());

            summary.TenantsAttempted.Should().Be(0);
            summary.Results.Should().BeEmpty();
            _dbContextProvider.Verify(d => d.GetCollection<Tenant>(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task PropagateAsync_PermissionNotBuiltIn_Skips()
        {
            _resourceRepo.Setup(r => r.GetPermissionByIdAsync("perm-1"))
                .ReturnsAsync(new Permission { ItemId = "perm-1", Resource = "res", IsBuiltIn = false });

            var summary = await CreateSut().PropagateAsync(Event());

            summary.TenantsAttempted.Should().Be(0);
            _dbContextProvider.Verify(d => d.GetCollection<Tenant>(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task PropagateAsync_MissingSourceTenant_Skips()
        {
            SetContext(null);
            _resourceRepo.Setup(r => r.GetPermissionByIdAsync("perm-1"))
                .ReturnsAsync(new Permission { ItemId = "perm-1", Resource = "res", IsBuiltIn = true });

            var summary = await CreateSut().PropagateAsync(Event());

            summary.TenantsAttempted.Should().Be(0);
            _dbContextProvider.Verify(d => d.GetCollection<Tenant>(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task PropagateAsync_NoEnabledTenants_ReturnsSummaryWithoutAttempts()
        {
            _resourceRepo.Setup(r => r.GetPermissionByIdAsync("perm-1"))
                .ReturnsAsync(new Permission { ItemId = "perm-1", Resource = "res", IsBuiltIn = true });
            _rootDb.Setup(d => d.GetCollection<Tenant>("Tenants", null))
                .Returns(MockCollection(new List<Tenant>()).Object);

            var summary = await CreateSut().PropagateAsync(Event());

            summary.TenantsAttempted.Should().Be(0);
            summary.Results.Should().BeEmpty();
        }

        // ---------- PropagateAsync: attempted + failed tenant branch ----------

        [Fact]
        public async Task PropagateAsync_TargetWithUnusableConnection_RecordsFailure()
        {
            _resourceRepo.Setup(r => r.GetPermissionByIdAsync("perm-1"))
                .ReturnsAsync(new Permission { ItemId = "perm-1", Resource = "res", IsBuiltIn = true });
            var tenant = MakeTenant("target-1", "Target One", isRoot: false, isDisabled: false,
                connString: "this-is-not-a-valid-connection-string", dbName: "TargetDb");
            _rootDb.Setup(d => d.GetCollection<Tenant>("Tenants", null))
                .Returns(MockCollection(new List<Tenant> { tenant }).Object);

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Update));

            summary.TenantsAttempted.Should().Be(1);
            summary.TenantsFailed.Should().Be(1);
            summary.TenantsSucceeded.Should().Be(0);
            summary.Results.Should().ContainSingle();
            var r = summary.Results[0];
            r.Success.Should().BeFalse();
            r.TenantId.Should().Be("target-1");
            r.ErrorMessage.Should().NotBeNullOrEmpty();
        }

        // ---------- GetTargetsAsync ----------

        [Fact]
        public async Task GetTargetsAsync_MapsRootAndNonRootAndSkipsIneligible()
        {
            var tenants = new List<Tenant>
            {
                MakeTenant("root-1", "Root", isRoot: true, isDisabled: false, connString: "mongodb://root"),
                MakeTenant("t-2", "T2", isRoot: false, isDisabled: false, connString: "mongodb://t2", dbName: "Db2"),
                MakeTenant("t-3", "Disabled", isRoot: false, isDisabled: true, connString: "mongodb://t3", dbName: "Db3"),
                MakeTenant("t-4", "NoConn", isRoot: false, isDisabled: false, connString: "", dbName: "Db4"),
                MakeTenant("t-5", "NoDb", isRoot: false, isDisabled: false, connString: "mongodb://t5", dbName: ""),
                MakeTenant("", "NoId", isRoot: false, isDisabled: false, connString: "mongodb://x", dbName: "Db6")
            };
            _rootDb.Setup(d => d.GetCollection<Tenant>("Tenants", null))
                .Returns(MockCollection(tenants).Object);

            var targets = await CreateSut().GetTargetsAsync(SourceTenantId);

            targets.Should().HaveCount(2);
            _dbContextProvider.Verify(p => p.GetDatabase(_secret.DatabaseConnectionString, _secret.RootDatabaseName, false), Times.Once);
            _dbContextProvider.Verify(p => p.GetCollection<Tenant>(It.IsAny<string>()), Times.Never);
            targets.Single(t => t.TenantId == "root-1").DbConnectionString.Should().Be(_secret.DatabaseConnectionString);
            targets.Should().ContainSingle(t => t.TenantId == "root-1" && t.DBName == "BlocksConfiguration");
            targets.Should().ContainSingle(t => t.TenantId == "t-2" && t.DBName == "Db2");
        }

        [Fact]
        public async Task GetTargetsAsync_WhenEnumerationThrows_FailsSoWorkerCanRetry()
        {
            _rootDb.Setup(d => d.GetCollection<Tenant>("Tenants", null))
                .Throws(new InvalidOperationException("db unavailable"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().GetTargetsAsync(SourceTenantId));
        }

        // ---------- OpenDatabase ----------

        [Fact]
        public void OpenDatabase_EmptyConnectionString_Throws()
        {
            Action act = () => CreateSut().OpenDatabase("", "db");

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void OpenDatabase_EmptyDatabaseName_Throws()
        {
            Action act = () => CreateSut().OpenDatabase("mongodb://localhost:27017", "   ");

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void OpenDatabase_ValidArguments_DelegatesToSharedProvider()
        {
            var database = new Mock<IMongoDatabase>();
            _dbContextProvider.Setup(p => p.GetDatabase("mongodb://localhost:27017", "testdb", false)).Returns(database.Object);
            var sut = CreateSut();

            var db1 = sut.OpenDatabase("mongodb://localhost:27017", "testdb");
            var db2 = sut.OpenDatabase("mongodb://localhost:27017", "testdb");

            db1.Should().BeSameAs(database.Object);
            db2.Should().BeSameAs(database.Object);
            _dbContextProvider.Verify(p => p.GetDatabase("mongodb://localhost:27017", "testdb", false), Times.Exactly(2));
        }
    }
}
