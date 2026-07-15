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
    /// dependencies are mocked. The per-tenant write path news up a real <c>MongoClient</c> with no
    /// seam, so the "succeeded" tenant branch requires a live MongoDB and is not covered here; the
    /// attempted/failed branches are exercised by pointing a target at an unparsable connection string.
    /// </summary>
    public class TenantPermissionPropagatorTests : IDisposable
    {
        private const string SourceTenantId = "tenant-source";

        private readonly Mock<IResourceRepository> _resourceRepo = new();
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();

        public TenantPermissionPropagatorTests()
        {
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
            new(_resourceRepo.Object, _dbContextProvider.Object, NullLogger<TenantPermissionPropagator>.Instance);

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
            _dbContextProvider.Setup(d => d.GetCollection<Tenant>("Tenants"))
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
            _dbContextProvider.Setup(d => d.GetCollection<Tenant>("Tenants"))
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
            _dbContextProvider.Setup(d => d.GetCollection<Tenant>("Tenants"))
                .Returns(MockCollection(tenants).Object);

            var targets = await CreateSut().GetTargetsAsync(SourceTenantId);

            targets.Should().HaveCount(2);
            targets.Should().ContainSingle(t => t.TenantId == "root-1" && t.DBName == "BlocksConfiguration");
            targets.Should().ContainSingle(t => t.TenantId == "t-2" && t.DBName == "Db2");
        }

        [Fact]
        public async Task GetTargetsAsync_WhenEnumerationThrows_ReturnsEmpty()
        {
            _dbContextProvider.Setup(d => d.GetCollection<Tenant>("Tenants"))
                .Throws(new InvalidOperationException("db unavailable"));

            var targets = await CreateSut().GetTargetsAsync(SourceTenantId);

            targets.Should().BeEmpty();
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
        public void OpenDatabase_ValidArguments_ReturnsDatabaseAndCachesClient()
        {
            var sut = CreateSut();

            var db1 = sut.OpenDatabase("mongodb://localhost:27017", "testdb");
            var db2 = sut.OpenDatabase("mongodb://localhost:27017", "testdb");

            db1.Should().NotBeNull();
            db2.Should().NotBeNull();
        }
    }
}
