using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.TenantPropagation;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.IamTests.Resources
{
    /// <summary>
    /// Integration tests for <see cref="TenantPermissionPropagator"/> using a provider backed by a
    /// real MongoClient. The create, update and archive paths run against a local mongod,
    /// each test using its own throwaway
    /// database named from a <see cref="Guid"/> which is dropped in <see cref="Dispose"/>.
    ///
    /// Requires a MongoDB reachable at mongodb://localhost:27017.
    /// </summary>
    public class TenantPermissionPropagatorIntegrationTests : IDisposable
    {
        private const string ConnectionString = "mongodb://localhost:27017";
        private const string SourceTenantId = "tenant-source";
        private const string Resource = "it.resource.read";

        private readonly Mock<IResourceRepository> _resourceRepo = new();
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IMongoDatabase> _rootDb = new();
        private readonly BlocksSecret _secret = new()
        {
            DatabaseConnectionString = "mongodb://localhost:27017",
            RootDatabaseName = "BlocksRootDb"
        };
        private readonly MongoClient _client = new(ConnectionString);
        private readonly List<string> _databases = new();

        public TenantPermissionPropagatorIntegrationTests()
        {
            _dbContextProvider.Setup(p => p.GetDatabase(It.IsAny<string>(), It.IsAny<string>(), false))
                .Returns((string connection, string database, bool _) => new MongoClient(connection).GetDatabase(database));
            _dbContextProvider.Setup(p => p.GetDatabase(_secret.DatabaseConnectionString, _secret.RootDatabaseName, false))
                .Returns(_rootDb.Object);
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: SourceTenantId, roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: SourceTenantId, impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            foreach (var db in _databases)
            {
                try { _client.DropDatabase(db); } catch { /* best-effort cleanup */ }
            }

            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
            GC.SuppressFinalize(this);
        }

        private string NewDatabaseName()
        {
            var name = "blocks_iam_it_" + Guid.NewGuid().ToString("N");
            _databases.Add(name);
            return name;
        }

        private TenantPermissionPropagator CreateSut() =>
            new(_resourceRepo.Object, _dbContextProvider.Object, NullLogger<TenantPermissionPropagator>.Instance, _secret);

        private static Permission BuiltInPermission() => new()
        {
            ItemId = "perm-1",
            Name = "Read",
            Description = "Read resource",
            Resource = Resource,
            ResourceGroup = "grp",
            Type = ResourceType.Endpoint,
            PermissionSeverity = PermissionSeverity.Low,
            IsBuiltIn = true,
            IsArchived = false,
            Tags = new List<string> { "t1" },
            DependentPermissions = new List<string> { "dep-1" }
        };

        private void SetupSourcePermission() =>
            _resourceRepo.Setup(r => r.GetPermissionByIdAsync("perm-1")).ReturnsAsync(BuiltInPermission());

        private void SetupSingleTarget(string dbName)
        {
            var tenant = new Tenant
            {
                TenantId = "target-1",
                Name = "Target One",
                IsRootTenant = false,
                IsDisabled = false,
                DbConnectionString = ConnectionString,
                DBName = dbName,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
            };
            _rootDb.Setup(d => d.GetCollection<Tenant>("Tenants", null))
                .Returns(MongoMock.Collection(new List<Tenant> { tenant }).Object);
        }

        private IMongoCollection<BsonDocument> Permissions(string dbName) =>
            _client.GetDatabase(dbName).GetCollection<BsonDocument>("Permissions");

        private static PermissionMutationForTenantsEvent Event(MutationEventType action) =>
            new() { ItemId = "perm-1", Action = action };

        // ---------- Create ----------

        [Fact]
        public async Task PropagateAsync_Create_InsertsPermissionIntoTenantDatabase()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            summary.TenantsAttempted.Should().Be(1);
            summary.TenantsSucceeded.Should().Be(1);
            summary.TenantsFailed.Should().Be(0);
            summary.Results.Should().ContainSingle();
            summary.Results[0].DocumentsAffected.Should().Be(1);

            var stored = await Permissions(dbName).Find(Builders<BsonDocument>.Filter.Eq("Resource", Resource)).FirstOrDefaultAsync();
            stored.Should().NotBeNull();
            stored["Name"].AsString.Should().Be("Read");
            stored["IsBuiltIn"].AsBoolean.Should().BeTrue();
        }

        [Fact]
        public async Task PropagateAsync_Create_WhenAlreadyPresent_ReturnsZeroAffectedOnDuplicateKey()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);

            // First propagation inserts the doc; the second hits the DuplicateKey catch and returns 0.
            await CreateSut().PropagateAsync(Event(MutationEventType.Create));
            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            summary.TenantsSucceeded.Should().Be(1);
            summary.Results.Should().ContainSingle();
            summary.Results[0].DocumentsAffected.Should().Be(0);
        }

        [Fact]
        public async Task PropagateAsync_Create_MultiOrgEnabledWithNoOrganizations_ReturnsOne()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);

            await _client.GetDatabase(dbName).GetCollection<BsonDocument>("TenantConfigurations")
                .InsertOneAsync(new BsonDocument { { "_id", "cfg-1" }, { "IsMultiOrgEnabled", true } });

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            summary.TenantsSucceeded.Should().Be(1);
            summary.Results[0].DocumentsAffected.Should().Be(1);
        }

        [Fact]
        public async Task PropagateAsync_Create_MultiOrgEnabledWithOrganizations_InsertsOneRowPerOrganization()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);

            await _client.GetDatabase(dbName).GetCollection<BsonDocument>("TenantConfigurations")
                .InsertOneAsync(new BsonDocument { { "_id", "cfg-1" }, { "IsMultiOrgEnabled", true } });
            await SeedOrganizationsAsync(dbName);

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            summary.TenantsSucceeded.Should().Be(1);
            summary.Results[0].DocumentsAffected.Should().Be(2);
            var rows = await PermissionRowsAsync(dbName);
            rows.Should().HaveCount(3);
            rows.Select(r => r["_id"].AsString).Distinct().Should().HaveCount(3);
            rows.Single(r => r["OrganizationId"] == "default")["_id"].AsString.Should().Be("perm-1");
            foreach (var org in new[] { "org-1", "org-2" })
            {
                var row = rows.Single(r => r["OrganizationId"] == org);
                row["ItemId"].AsString.Should().Be(row["_id"].AsString);
            }
        }

        [Fact]
        public async Task PropagateAsync_Create_MultiOrgEnabledWithOrganizations_RerunAddsNoDuplicates()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);

            await _client.GetDatabase(dbName).GetCollection<BsonDocument>("TenantConfigurations")
                .InsertOneAsync(new BsonDocument { { "_id", "cfg-1" }, { "IsMultiOrgEnabled", true } });
            await SeedOrganizationsAsync(dbName);

            await CreateSut().PropagateAsync(Event(MutationEventType.Create));
            var second = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            second.TenantsSucceeded.Should().Be(1);
            second.Results[0].DocumentsAffected.Should().Be(0);
            (await PermissionRowsAsync(dbName)).Should().HaveCount(3);
        }

        private Task<List<BsonDocument>> PermissionRowsAsync(string dbName) =>
            _client.GetDatabase(dbName).GetCollection<BsonDocument>("Permissions")
                .Find(Builders<BsonDocument>.Filter.Eq("Resource", Resource)).ToListAsync();

        // ---------- IsMultiOrgEnabled read through TenantConfigurationStore (SPEC1 3.3, C16 / I8) ----------

        private Task SeedOrganizationsAsync(string dbName) =>
            _client.GetDatabase(dbName).GetCollection<BsonDocument>("Organizations").InsertManyAsync(new[]
            {
                new BsonDocument { { "_id", "org-1" }, { "ItemId", "org-1" }, { "Name", "Org 1" } },
                new BsonDocument { { "_id", "org-2" }, { "ItemId", "org-2" }, { "Name", "Org 2" } }
            });

        [Fact]
        public async Task PropagateAsync_Create_MultiOrgFlagOnlyInTheIdentityField_ReachesTheOrganizationBranch()
        {
            // After seed/tenant cleanup the old TenantConfigurations collection is gone and only the field
            // exists. The propagator must still see IsMultiOrgEnabled and go on to the organizations (the
            // same branch as the old-collection test above).
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);
            await _client.GetDatabase(dbName).GetCollection<BsonDocument>("IdentityConfigurations")
                .InsertOneAsync(new BsonDocument
                {
                    { "_id", ObjectId.GenerateNewId() },
                    { "TenantConfiguration", new BsonDocument { { "_id", "cfg-1" }, { "IsMultiOrgEnabled", true } } }
                });
            await SeedOrganizationsAsync(dbName);

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            summary.TenantsSucceeded.Should().Be(1);   // org branch reached: one row per org
            summary.Results[0].DocumentsAffected.Should().Be(2);
            (await _client.GetDatabase(dbName).ListCollectionNames().ToListAsync()).Should().NotContain("TenantConfigurations");
        }

        [Fact]
        public async Task PropagateAsync_Create_FieldMissing_ReadsTheOldCollection()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);
            await _client.GetDatabase(dbName).GetCollection<BsonDocument>("IdentityConfigurations")
                .InsertOneAsync(new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "Other", 1 } });
            await _client.GetDatabase(dbName).GetCollection<BsonDocument>("TenantConfigurations")
                .InsertOneAsync(new BsonDocument { { "_id", "cfg-1" }, { "IsMultiOrgEnabled", true } });
            await SeedOrganizationsAsync(dbName);

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            summary.TenantsSucceeded.Should().Be(1);   // org branch reached via the fallback read
            summary.Results[0].DocumentsAffected.Should().Be(2);
        }

        [Fact]
        public async Task PropagateAsync_Create_FieldSaysSingleOrg_DoesNotPropagateToOrganizations()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);
            await _client.GetDatabase(dbName).GetCollection<BsonDocument>("IdentityConfigurations")
                .InsertOneAsync(new BsonDocument
                {
                    { "_id", ObjectId.GenerateNewId() },
                    { "TenantConfiguration", new BsonDocument { { "_id", "cfg-1" }, { "IsMultiOrgEnabled", false } } }
                });
            await SeedOrganizationsAsync(dbName);

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            summary.TenantsSucceeded.Should().Be(1);
            summary.Results[0].DocumentsAffected.Should().Be(1);
        }

        [Fact]
        public async Task PropagateAsync_Create_NoConfigurationAnywhere_DoesNotPropagateToOrganizations_AsToday()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);
            await SeedOrganizationsAsync(dbName);

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            summary.TenantsSucceeded.Should().Be(1);
            summary.Results[0].DocumentsAffected.Should().Be(1);
            (await _client.GetDatabase(dbName).ListCollectionNames().ToListAsync()).Should().NotContain("TenantConfigurations");
        }

        // ---------- Update ----------

        [Fact]
        public async Task PropagateAsync_Update_ModifiesMatchingDocuments()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);

            await Permissions(dbName).InsertOneAsync(new BsonDocument
            {
                { "_id", "existing-1" },
                { "Resource", Resource },
                { "Name", "old" },
                { "IsArchived", false }
            });

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Update));

            summary.TenantsSucceeded.Should().Be(1);
            summary.Results[0].DocumentsAffected.Should().Be(1);

            var stored = await Permissions(dbName).Find(Builders<BsonDocument>.Filter.Eq("_id", "existing-1")).FirstOrDefaultAsync();
            stored["Name"].AsString.Should().Be("Read");
            stored["Description"].AsString.Should().Be("Read resource");
        }

        // ---------- Delete ----------

        [Fact]
        public async Task PropagateAsync_Delete_ArchivesMatchingDocuments()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);

            await Permissions(dbName).InsertOneAsync(new BsonDocument
            {
                { "_id", "existing-1" },
                { "Resource", Resource },
                { "IsArchived", false }
            });

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Delete));

            summary.TenantsSucceeded.Should().Be(1);
            summary.Results[0].DocumentsAffected.Should().Be(1);

            var stored = await Permissions(dbName).Find(Builders<BsonDocument>.Filter.Eq("_id", "existing-1")).FirstOrDefaultAsync();
            stored["IsArchived"].AsBoolean.Should().BeTrue();
        }

        // ---------- Unsupported action ----------

        [Fact]
        public async Task PropagateAsync_NoneAction_SkipsTenantWithoutResult()
        {
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.None));

            summary.TenantsAttempted.Should().Be(1);
            summary.TenantsSucceeded.Should().Be(0);
            summary.TenantsFailed.Should().Be(0);
            summary.Results.Should().BeEmpty();
        }
    }
}
