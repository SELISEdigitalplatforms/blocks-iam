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

namespace XUnitTest.IamTests.Resources
{
    /// <summary>
    /// Integration-style tests for <see cref="TenantPermissionPropagator"/>. The per-tenant write path
    /// news up a real <c>MongoClient</c> through <c>OpenDatabase</c> with no seam, so the "succeeded"
    /// tenant branches (ApplyInsertAsync, ApplyUpdateAsync and the Delete archive) can only be covered
    /// against a live MongoDB. These run against a local mongod, each test using its own throwaway
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
        private readonly MongoClient _client = new(ConnectionString);
        private readonly List<string> _databases = new();

        public TenantPermissionPropagatorIntegrationTests()
        {
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
            new(_resourceRepo.Object, _dbContextProvider.Object, NullLogger<TenantPermissionPropagator>.Instance);

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
            _dbContextProvider.Setup(d => d.GetCollection<Tenant>("Tenants"))
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
        public async Task PropagateAsync_Create_MultiOrgEnabledWithOrganizations_RecordsFailureFromDuplicateId()
        {
            // The multi-org branch reuses source.ItemId as the Bson _id for every per-org document, so the
            // InsertMany collides with the base document just inserted and MongoDB rejects the batch. The
            // per-org "succeeded" path is therefore not reachable against a real Mongo; this asserts the
            // real observed behavior (a recorded tenant failure) rather than the intended one.
            var dbName = NewDatabaseName();
            SetupSourcePermission();
            SetupSingleTarget(dbName);

            await _client.GetDatabase(dbName).GetCollection<BsonDocument>("TenantConfigurations")
                .InsertOneAsync(new BsonDocument { { "_id", "cfg-1" }, { "IsMultiOrgEnabled", true } });
            await _client.GetDatabase(dbName).GetCollection<BsonDocument>("Organizations").InsertManyAsync(new[]
            {
                new BsonDocument { { "_id", "org-1" }, { "ItemId", "org-1" }, { "Name", "Org 1" } },
                new BsonDocument { { "_id", "org-2" }, { "ItemId", "org-2" }, { "Name", "Org 2" } }
            });

            var summary = await CreateSut().PropagateAsync(Event(MutationEventType.Create));

            summary.TenantsAttempted.Should().Be(1);
            summary.TenantsFailed.Should().Be(1);
            summary.Results[0].Success.Should().BeFalse();
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
