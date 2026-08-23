using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Resources
{
    /// <summary>
    /// Unit tests for <see cref="ResourceRepository"/>. The underlying Mongo access is reached through
    /// <see cref="IIdentityAccessManagementRepository.GetCollection{T}()"/> and
    /// <see cref="IIdentityAccessManagementRepository.GetCollectionByName{T}(string)"/>, both mocked to
    /// return in-memory collections so filter construction, projection and result mapping are exercised.
    /// </summary>
    public class ResourceRepositoryTests : IDisposable
    {
        private readonly Mock<IIdentityAccessManagementRepository> _iam = new();

        public ResourceRepositoryTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private ResourceRepository Sut() => new(_iam.Object);

        private Mock<IMongoCollection<T>> Register<T>(IEnumerable<T>? items = null)
        {
            var col = MongoMock.Collection(items);
            _iam.Setup(r => r.GetCollection<T>()).Returns(col.Object);
            _iam.Setup(r => r.GetCollection<T>(It.IsAny<string>())).Returns(col.Object);
            _iam.Setup(r => r.GetCollectionByName<T>(It.IsAny<string>())).Returns(col.Object);
            return col;
        }

        private static Permission Perm(string id, string resource = "res", string org = "default",
            PermissionSeverity severity = PermissionSeverity.Low, ResourceType type = ResourceType.Endpoint,
            string? group = "grp", IEnumerable<string>? roles = null) =>
            new() { ItemId = id, Resource = resource, OrganizationId = org, PermissionSeverity = severity, Type = type, ResourceGroup = group, Name = "n-" + id, Description = "d", Roles = (roles ?? Enumerable.Empty<string>()).ToList() };

        private static Role RoleE(string id, string slug = "slug", string org = "default") =>
            new() { ItemId = id, Slug = slug, OrganizationId = org, Name = "role-" + id, Description = "d" };

        [Fact]
        public async Task GetPermissionByResourceAsync_ReturnsMatch_WithOrgFilter()
        {
            Register(new[] { Perm("p1") });
            var result = await Sut().GetPermissionByResourceAsync("res", "default");
            result.ItemId.Should().Be("p1");
        }

        [Fact]
        public async Task GetPermissionByResourceAsync_WithEmptyOrg_SkipsOrgFilter()
        {
            Register(new[] { Perm("p1") });
            var result = await Sut().GetPermissionByResourceAsync("res", "");
            result.ItemId.Should().Be("p1");
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ReturnsList()
        {
            Register(new[] { Perm("p1"), Perm("p2") });
            var result = await Sut().GetPermissionsByResourcesAsync(new List<string> { "res" }, "default");
            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_EmptyOrg_SkipsOrgFilter()
        {
            Register(new[] { Perm("p1") });
            var result = await Sut().GetPermissionsByResourcesAsync(new List<string> { "res" }, "");
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetPermissionByIdAsync_ReturnsMatch()
        {
            Register(new[] { Perm("p9") });
            (await Sut().GetPermissionByIdAsync("p9")).ItemId.Should().Be("p9");
        }

        [Fact]
        public async Task InsertPermissionAsync_InsertsAndReturnsTrue()
        {
            var col = Register<Permission>();
            (await Sut().InsertPermissionAsync(Perm("p1"))).Should().BeTrue();
            col.Verify(c => c.InsertOneAsync(It.IsAny<Permission>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdatePermissionAsync_ReturnsAcknowledged()
        {
            Register<Permission>();
            (await Sut().UpdatePermissionAsync(Perm("p1"))).Should().BeTrue();
        }

        [Fact]
        public async Task GetRoleByIdAsync_ReturnsMatch()
        {
            Register(new[] { RoleE("r1") });
            (await Sut().GetRoleByIdAsync("r1")).ItemId.Should().Be("r1");
        }

        [Fact]
        public async Task InsertRoleAsync_ReturnsTrue()
        {
            var col = Register<Role>();
            (await Sut().InsertRoleAsync(RoleE("r1"))).Should().BeTrue();
            col.Verify(c => c.InsertOneAsync(It.IsAny<Role>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateRoleAsync_ReturnsAcknowledged()
        {
            Register<Role>();
            (await Sut().UpdateRoleAsync(RoleE("r1"))).Should().BeTrue();
        }

        [Fact]
        public async Task GetPermissionsGroupBySeverityAsync_GroupsAndCounts()
        {
            Register(new[]
            {
                Perm("p1", severity: PermissionSeverity.Low),
                Perm("p2", severity: PermissionSeverity.Low),
                Perm("p3", severity: PermissionSeverity.High)
            });
            var result = await Sut().GetPermissionsGroupBySeverityAsync("default");
            result.Should().HaveCount(2);
            result.Single(r => r.SeverityLevel == PermissionSeverity.Low.ToString()).Count.Should().Be(2);
        }

        [Fact]
        public async Task GetPermissionsAsync_AppliesFiltersAndReturnsCount()
        {
            var col = Register(new[] { Perm("p1") });
            MongoMock.SetupCount(col, 5);
            var query = new GetPermissionsRequest
            {
                Page = 0,
                PageSize = 10,
                Sort = new BaseSortRequest { Property = "Name", IsDescending = true },
                Filter = new GetPermissionFilter
                {
                    IsArchived = false,
                    Type = ResourceType.Endpoint,
                    PermissionSeverity = PermissionSeverity.Low,
                    Search = "abc",
                    IsBuiltIn = "yes",
                    Tags = new List<string> { "t1" },
                    ResourceGroup = "grp",
                    Resources = new List<string> { "res" }
                }
            };
            var (items, count) = await Sut().GetPermissionsAsync(query, "default");
            count.Should().Be(5);
            items.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetPermissionsAsync_NoFilter_Works()
        {
            Register(new[] { Perm("p1") });
            var (items, _) = await Sut().GetPermissionsAsync(new GetPermissionsRequest());
            items.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetPermissionsAsync_FiltersByRoles()
        {
            var col = Register(new[]
            {
                Perm("p1", roles: new List<string> { "admin" })
            });
            MongoMock.SetupCount(col, 1);

            var query = new GetPermissionsRequest
            {
                Roles = new List<string> { "admin" }
            };

            var (items, count) = await Sut().GetPermissionsAsync(query, "default");

            count.Should().Be(1);
            items.Should().HaveCount(1);
            items.Single().ItemId.Should().Be("p1");
        }

        [Fact]
        public async Task GetRoleBySlugAsync_ReturnsMatch()
        {
            Register(new[] { RoleE("r1", "admin") });
            (await Sut().GetRoleBySlugAsync("admin", "default")).Slug.Should().Be("admin");
        }

        [Fact]
        public async Task GetRolesAsync_AppliesFiltersAndReturnsCount()
        {
            var col = Register(new[] { RoleE("r1") });
            MongoMock.SetupCount(col, 3);
            var query = new GetRolesRequest
            {
                Sort = new BaseSortRequest { Property = "Name", IsDescending = false },
                Filter = new GetRolesFilter { Search = "adm", Slugs = new List<string> { "admin" } }
            };
            var (items, count) = await Sut().GetRolesAsync(query, "default");
            count.Should().Be(3);
            items.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRolesAsync_NoFilter_Works()
        {
            Register(new[] { RoleE("r1") });
            var (items, _) = await Sut().GetRolesAsync(new GetRolesRequest());
            items.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRoleBySlugAndOrgAsync_ReturnsMatch()
        {
            Register(new[] { RoleE("r1", "admin", "org1") });
            (await Sut().GetRoleBySlugAndOrgAsync("admin", "org1")).ItemId.Should().Be("r1");
        }

        [Fact]
        public async Task GetRolesBySlugAndOrgAsync_ReturnsList()
        {
            Register(new[] { RoleE("r1"), RoleE("r2") });
            (await Sut().GetRolesBySlugAndOrgAsync(new List<string> { "a" }, "org1")).Should().HaveCount(2);
        }

        [Fact]
        public async Task InsertRolesAsync_ReturnsTrue()
        {
            var col = Register<Role>();
            (await Sut().InsertRolesAsync(new List<Role> { RoleE("r1") })).Should().BeTrue();
            col.Verify(c => c.InsertManyAsync(It.IsAny<IEnumerable<Role>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetRolesByOrgAsync_ReturnsList()
        {
            Register(new[] { RoleE("r1") });
            (await Sut().GetRolesByOrgAsync("org1")).Should().HaveCount(1);
        }

        [Fact]
        public async Task UpdateRolePermissionByIdsAsync_ReturnsAcknowledged()
        {
            Register<BsonDocument>();
            (await Sut().UpdateRolePermissionByIdsAsync("admin", new List<string> { "p1" }, "default")).Should().BeTrue();
        }

        [Fact]
        public async Task RemoveRolePermissionByIdsAsync_ReturnsAcknowledged()
        {
            Register<BsonDocument>();
            (await Sut().RemoveRolePermissionByIdsAsync("admin", new List<string> { "p1" }, "default")).Should().BeTrue();
        }

        /// <summary>
        /// The usage count is derived by reading the raw <c>Permissions</c> collection and matching the
        /// role slug per document, so the documents are registered as <see cref="BsonDocument"/> in the
        /// two shapes the repository understands: see <see cref="PermDoc"/> and <see cref="PermDocByOrg"/>.
        /// </summary>
        private Mock<IMongoCollection<BsonDocument>> RegisterPermissionDocuments(params BsonDocument[] documents)
        {
            var col = MongoMock.Collection(documents.AsEnumerable());
            _iam.Setup(r => r.GetCollectionByName<BsonDocument>("Permissions")).Returns(col.Object);
            return col;
        }

        /// <summary>A permission whose <c>Roles</c> is a flat array, owned by a single organization.</summary>
        private static BsonDocument PermDoc(string id, string org, params string[] roles) =>
            new() { { "_id", id }, { "OrganizationId", org }, { "Roles", new BsonArray(roles) } };

        /// <summary>A permission whose <c>Roles</c> is a map of organization id to the roles granted there.</summary>
        private static BsonDocument PermDocByOrg(string id, params (string Org, string[] Roles)[] rolesByOrg)
        {
            var map = new BsonDocument();
            foreach (var (org, roles) in rolesByOrg)
            {
                map.Add(org, new BsonArray(roles));
            }
            return new BsonDocument { { "_id", id }, { "Roles", map } };
        }

        /// <summary>Capture the <c>Count</c> the repository writes, so the tests assert the tallied value.</summary>
        private static long CapturedCount(UpdateDefinition<Role>? update)
        {
            update.Should().NotBeNull();
            var registry = BsonSerializer.SerializerRegistry;
            var rendered = update!.Render(new RenderArgs<Role>(registry.GetSerializer<Role>(), registry));
            return rendered["$set"]["Count"].ToInt64();
        }

        private static Mock<IMongoCollection<Role>> CaptureRoleUpdate(
            Mock<IMongoCollection<Role>> roles, Action<UpdateDefinition<Role>> capture)
        {
            roles.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<Role>>(), It.IsAny<UpdateDefinition<Role>>(),
                    It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<Role>, UpdateDefinition<Role>, UpdateOptions, CancellationToken>(
                    (_, update, _, _) => capture(update))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
            return roles;
        }

        [Fact]
        public async Task UpdateRolesCountAsync_CountsZeroWhenNoPermissionReferencesTheRole()
        {
            RegisterPermissionDocuments();
            UpdateDefinition<Role>? written = null;
            var roles = CaptureRoleUpdate(Register<Role>(), u => written = u);

            (await Sut().UpdateRolesCountAsync("admin", "default")).Should().BeTrue();

            // The role is still written, so a role that lost its last permission is reset to 0
            // rather than keeping a stale count.
            roles.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<Role>>(), It.IsAny<UpdateDefinition<Role>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
            CapturedCount(written).Should().Be(0);
        }

        [Fact]
        public async Task UpdateRolesCountAsync_ReturnsFalseWhenTheRoleUpdateIsNotAcknowledged()
        {
            RegisterPermissionDocuments(PermDoc("p1", "default", "admin"));
            var roles = Register<Role>();
            roles.Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<Role>>(), It.IsAny<UpdateDefinition<Role>>(),
                    It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(UpdateResult.Unacknowledged.Instance);

            (await Sut().UpdateRolesCountAsync("admin", "default")).Should().BeFalse();
        }

        [Fact]
        public async Task GetResourceGroupsAsync_GroupsByResourceGroup()
        {
            Register(new[]
            {
                Perm("p1", group: "g1"),
                Perm("p2", group: "g1"),
                Perm("p3", group: "g2")
            });
            var result = await Sut().GetResourceGroupsAsync("default");
            result.Should().HaveCount(2);
            result.Single(r => r.ResourceGroup == "g1").Count.Should().Be(2);
        }

        [Fact]
        public async Task GetOrganizationById_ReturnsMatch()
        {
            Register(new[] { new Organization { ItemId = "o1", Name = "Org" } });
            (await Sut().GetOrganizationById("o1")).ItemId.Should().Be("o1");
        }

        [Fact]
        public async Task GetOrganizationByNameAsync_NullOrWhitespace_ReturnsNull()
        {
            (await Sut().GetOrganizationByNameAsync("  ")).Should().BeNull();
        }

        [Fact]
        public async Task GetOrganizationByNameAsync_ReturnsMatch()
        {
            Register(new[] { new Organization { ItemId = "o1", Name = "Org" } });
            (await Sut().GetOrganizationByNameAsync("Org")).ItemId.Should().Be("o1");
        }

        [Fact]
        public async Task GetOrganizationIdsByUserIdAsync_EmptyUser_ReturnsEmpty()
        {
            (await Sut().GetOrganizationIdsByUserIdAsync(" ")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetOrganizationIdsByUserIdAsync_ReturnsDistinctNonEmpty()
        {
            var col = MongoMock.Collection<User>();
            MongoMock.SetupProjectedFind(col, new List<List<string>>
            {
                new() { "o1", "o1", "", "o2" }
            });
            _iam.Setup(r => r.GetCollection<User>()).Returns(col.Object);
            var result = await Sut().GetOrganizationIdsByUserIdAsync("u1");
            result.Should().BeEquivalentTo(new[] { "o1", "o2" });
        }

        [Fact]
        public async Task GetOrganizationsByIdsAsync_Null_ReturnsEmpty()
        {
            (await Sut().GetOrganizationsByIdsAsync(null!)).Should().BeEmpty();
        }

        [Fact]
        public async Task GetOrganizationsByIdsAsync_AllWhitespace_ReturnsEmpty()
        {
            (await Sut().GetOrganizationsByIdsAsync(new List<string> { " ", "" })).Should().BeEmpty();
        }

        [Fact]
        public async Task GetOrganizationsByIdsAsync_ReturnsList()
        {
            Register(new[] { new Organization { ItemId = "o1", Name = "Org" } });
            (await Sut().GetOrganizationsByIdsAsync(new List<string> { "o1", "o1" })).Should().HaveCount(1);
        }

        [Fact]
        public async Task SaveOrganizationAsync_Upserts()
        {
            var col = Register<Organization>();
            await Sut().SaveOrganizationAsync(new Organization { ItemId = "o1", Name = "Org" });
            col.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<Organization>>(), It.IsAny<Organization>(),
                It.Is<ReplaceOptions>(o => o.IsUpsert), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetOrganizationsAsync_AppliesFiltersAndReturnsResponse()
        {
            var col = Register(new[] { new Organization { ItemId = "o1", Name = "Org" } });
            MongoMock.SetupCount(col, 4);
            var request = new GetOrganizationsRequest
            {
                Sort = new BaseSortRequest { Property = "Name", IsDescending = true },
                Filter = new GetOrganizationsFilter
                {
                    Search = "org",
                    Ids = new List<string> { "o1" },
                    IsDisabled = false,
                    ParentOrganizationId = "parent"
                }
            };
            var result = await Sut().GetOrganizationsAsync(request);
            result.IsSuccess.Should().BeTrue();
            result.TotalCount.Should().Be(4);
            result.Organizations.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetOrganizationsAsync_NoFilter_DefaultSort()
        {
            Register(new[] { new Organization { ItemId = "o1", Name = "Org" } });
            var result = await Sut().GetOrganizationsAsync(new GetOrganizationsRequest());
            result.Organizations.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetTenantConfigurationAsync_Delegates()
        {
            var config = new TenantConfiguration { ItemId = "c1" };
            _iam.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(config);
            (await Sut().GetTenantConfigurationAsync()).ItemId.Should().Be("c1");
        }

        [Fact]
        public async Task SaveOrganizationConfig_Upserts()
        {
            var col = Register<TenantConfiguration>();
            await Sut().SaveOrganizationConfig(new TenantConfiguration { ItemId = "c1" });
            col.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<TenantConfiguration>>(), It.IsAny<UpdateDefinition<TenantConfiguration>>(),
                It.Is<UpdateOptions>(o => o.IsUpsert), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateAllSamePermissionAsync_ReturnsAcknowledged()
        {
            Register<Permission>();
            (await Sut().UpdateAllSamePermissionAsync(Perm("p1"))).Should().BeTrue();
        }

        [Fact]
        public async Task GetPermissionsByOrgAsync_WithoutPaging()
        {
            Register(new[] { Perm("p1") });
            (await Sut().GetPermissionsByOrgAsync("default")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetPermissionsByOrgAsync_WithPaging()
        {
            Register(new[] { Perm("p1") });
            (await Sut().GetPermissionsByOrgAsync("default", 2, 5)).Should().HaveCount(1);
        }

        [Fact]
        public async Task AddRoleToPermissionsByResourcesAsync_Guard_ReturnsTrue()
        {
            (await Sut().AddRoleToPermissionsByResourcesAsync("", new List<string>(), "org")).Should().BeTrue();
        }

        [Fact]
        public async Task AddRoleToPermissionsByResourcesAsync_Valid_ReturnsAcknowledged()
        {
            Register<Permission>();
            (await Sut().AddRoleToPermissionsByResourcesAsync("admin", new List<string> { "res" }, "org")).Should().BeTrue();
        }

        [Fact]
        public async Task RemoveRoleFromPermissionsByResourcesAsync_Guard_ReturnsTrue()
        {
            (await Sut().RemoveRoleFromPermissionsByResourcesAsync("admin", null!, "org")).Should().BeTrue();
        }

        [Fact]
        public async Task RemoveRoleFromPermissionsByResourcesAsync_Valid_ReturnsAcknowledged()
        {
            Register<Permission>();
            (await Sut().RemoveRoleFromPermissionsByResourcesAsync("admin", new List<string> { "res" }, "org")).Should().BeTrue();
        }

        [Fact]
        public async Task GetPermissionsByRoleAsync_ReturnsList()
        {
            Register(new[] { Perm("p1") });
            (await Sut().GetPermissionsByRoleAsync("admin", "org")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetFeResourceFeaturesAsync_NoRolesNoKeys_ReturnsEmpty()
        {
            (await Sut().GetFeResourceFeaturesAsync(new List<string>(), new List<string>())).Should().BeEmpty();
        }

        [Fact]
        public async Task GetFeResourceFeaturesAsync_WithRolesAndSearchAndBuiltIn()
        {
            Register(new[] { Perm("p1", type: ResourceType.FrontendAction) });
            var result = await Sut().GetFeResourceFeaturesAsync(
                new List<string> { "admin" }, new List<string>(), "search", true, "default");
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetFeResourceFeaturesAsync_WithPermissionKeysOnly()
        {
            Register(new[] { Perm("p1", type: ResourceType.FrontendAction) });
            var result = await Sut().GetFeResourceFeaturesAsync(
                new List<string>(), new List<string> { "key1" });
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetPermissionsByRolesAsync_ReturnsList()
        {
            Register(new[] { Perm("p1") });
            (await Sut().GetPermissionsByRolesAsync(new List<string> { "admin" }, "org", 2, 5)).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetPermissionsByGroupsAsync_ReturnsList()
        {
            Register(new[] { Perm("p1") });
            (await Sut().GetPermissionsByGroupsAsync(new List<string> { "g1" }, "org")).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetPermissionsByIdsAsync_ReturnsList()
        {
            Register(new[] { Perm("p1") });
            (await Sut().GetPermissionsByIdsAsync(new List<string> { "p1" })).Should().HaveCount(1);
        }

        [Fact]
        public async Task InsertPermissionsAsync_ReturnsTrue()
        {
            var col = Register<Permission>();
            (await Sut().InsertPermissionsAsync(new List<Permission> { Perm("p1") })).Should().BeTrue();
            col.Verify(c => c.InsertManyAsync(It.IsAny<IEnumerable<Permission>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetPermissionsByResourceAsync_Single_ReturnsList()
        {
            Register(new[] { Perm("p1") });
            (await Sut().GetPermissionsByResourceAsync("res")).Should().HaveCount(1);
        }

        [Fact]
        public async Task UpdatePermissionsAsync_Empty_ReturnsTrue()
        {
            (await Sut().UpdatePermissionsAsync(new List<Permission>())).Should().BeTrue();
        }

        [Fact]
        public async Task UpdatePermissionsAsync_WithItems_ReturnsAcknowledged()
        {
            var col = Register<Permission>();
            (await Sut().UpdatePermissionsAsync(new List<Permission> { Perm("p1") })).Should().BeTrue();
            col.Verify(c => c.BulkWriteAsync(It.IsAny<IEnumerable<WriteModel<Permission>>>(), It.IsAny<BulkWriteOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetRolesBySlugAsync_ReturnsList()
        {
            Register(new[] { RoleE("r1") });
            (await Sut().GetRolesBySlugAsync("slug")).Should().HaveCount(1);
        }

        [Fact]
        public async Task UpdateRolesAsync_Empty_ReturnsTrue()
        {
            (await Sut().UpdateRolesAsync(new List<Role>())).Should().BeTrue();
        }

        [Fact]
        public async Task UpdateRolesAsync_WithItems_ReturnsAcknowledged()
        {
            var col = Register<Role>();
            (await Sut().UpdateRolesAsync(new List<Role> { RoleE("r1") })).Should().BeTrue();
            col.Verify(c => c.BulkWriteAsync(It.IsAny<IEnumerable<WriteModel<Role>>>(), It.IsAny<BulkWriteOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------- Role archive (#456) ----------
        //
        // These assert the RENDERED filter/update rather than round-tripping rows, because the
        // shared MongoMock returns every configured item for any find and a preset value for any
        // count. A seeded-document test would therefore pass whatever the filter said -- including
        // after the Eq/Ne mistake these tests exist to catch.

        private static BsonDocument RenderFilter<T>(FilterDefinition<T>? filter)
        {
            filter.Should().NotBeNull();
            var registry = BsonSerializer.SerializerRegistry;
            return filter!.Render(new RenderArgs<T>(registry.GetSerializer<T>(), registry));
        }

        private static BsonDocument RenderUpdate<T>(UpdateDefinition<T>? update)
        {
            update.Should().NotBeNull();
            var registry = BsonSerializer.SerializerRegistry;
            return update!.Render(new RenderArgs<T>(registry.GetSerializer<T>(), registry)).AsBsonDocument;
        }

        [Fact]
        public async Task GetRolesAsync_HidesArchivedRolesWithNeNotEq()
        {
            FilterDefinition<Role>? captured = null;
            var col = Register(new[] { RoleE("r1") });
            col.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<Role>>(), It.IsAny<FindOptions<Role, Role>>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<Role>, FindOptions<Role, Role>, CancellationToken>((f, _, _) => captured = f)
                .ReturnsAsync(MongoMock.Cursor(new[] { RoleE("r1") }));

            await Sut().GetRolesAsync(new GetRolesRequest(), "default");

            var rendered = RenderFilter(captured);
            // $ne: true matches missing, null and false alike. $eq: false matches none of the role
            // documents written before this field existed, which would empty the list for every
            // tenant -- so its absence is the assertion that matters here.
            rendered.ToString().Should().Contain("\"IsArchived\" : { \"$ne\" : true }");
            rendered.ToString().Should().NotContain("\"IsArchived\" : false");
        }

        [Fact]
        public async Task GetRolesByOrgAsync_HidesArchivedRolesWithNeNotEq()
        {
            FilterDefinition<Role>? captured = null;
            var col = Register(new[] { RoleE("r1") });
            col.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<Role>>(), It.IsAny<FindOptions<Role, Role>>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<Role>, FindOptions<Role, Role>, CancellationToken>((f, _, _) => captured = f)
                .ReturnsAsync(MongoMock.Cursor(new[] { RoleE("r1") }));

            await Sut().GetRolesByOrgAsync("acme");

            // This query feeds CopyRoleFromDefault, so without the clause an archived default role
            // would be cloned into every organization provisioned afterwards.
            var rendered = RenderFilter(captured).ToString();
            rendered.Should().Contain("\"IsArchived\" : { \"$ne\" : true }");
            rendered.Should().NotContain("\"IsArchived\" : false");
        }

        [Fact]
        public async Task HasChildRolesAsync_FiltersOnParentOrgAndExcludesArchivedChildren()
        {
            FilterDefinition<Role>? captured = null;
            var col = Register<Role>();
            col.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<Role>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<Role>, CountOptions, CancellationToken>((f, _, _) => captured = f)
                .ReturnsAsync(1);

            (await Sut().HasChildRolesAsync("manager", "acme")).Should().BeTrue();

            var rendered = RenderFilter(captured).ToString();
            rendered.Should().Contain("\"ParentRoleSlug\" : \"manager\"");
            rendered.Should().Contain("\"OrganizationId\" : \"acme\"");
            // Without this an archived child keeps blocking, so a parent becomes permanently
            // unarchivable once its children are retired.
            rendered.Should().Contain("\"IsArchived\" : { \"$ne\" : true }");
        }

        [Fact]
        public async Task HasChildRolesAsync_NoMatches_ReturnsFalse()
        {
            var col = Register<Role>();
            col.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<Role>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            (await Sut().HasChildRolesAsync("manager", "acme")).Should().BeFalse();
        }

        [Fact]
        public async Task HasUserAssignmentsAsync_ScopesToOrgBucketAndTreatsMissingStatusAsActive()
        {
            FilterDefinition<User>? captured = null;
            var col = Register<User>();
            col.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<User>, CountOptions, CancellationToken>((f, _, _) => captured = f)
                .ReturnsAsync(1);

            (await Sut().HasUserAssignmentsAsync("manager", "acme")).Should().BeTrue();

            var filter = RenderFilter(captured);
            var rendered = filter.ToString();
            // The slug and the org bucket together: the mock counts whatever it is given, so
            // without asserting the slug this would pass for a filter that looked up a different
            // role entirely.
            rendered.Should().Contain("Roles.acme").And.Contain("manager");
            rendered.Should().Contain("\"Active\" : true");

            // The two status branches must be joined by $or, not $and. Asserting only that both
            // fragments appear would still pass for an $and -- which is unsatisfiable, since a user
            // cannot both have Status == Active and lack the field, so no active holder would ever
            // block an archive and every one of them would be silently scrubbed instead.
            filter.Contains("$or").Should().BeTrue("the two status branches must be alternatives");
            var statusClause = filter["$or"].AsBsonArray;

            statusClause.Should().HaveCount(2);
            statusClause.Select(x => x.AsBsonDocument["Status"].ToString())
                .Should().Contain("1").And.Contain(b => b.Contains("$exists"));
        }

        [Fact]
        public async Task RemoveRoleFromAllPermissionsAsync_PullsSlugScopedToOrganization()
        {
            FilterDefinition<Permission>? capturedFilter = null;
            UpdateDefinition<Permission>? capturedUpdate = null;
            var col = Register<Permission>();
            col.Setup(c => c.UpdateManyAsync(It.IsAny<FilterDefinition<Permission>>(), It.IsAny<UpdateDefinition<Permission>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<Permission>, UpdateDefinition<Permission>, UpdateOptions, CancellationToken>(
                    (f, u, _, _) => { capturedFilter = f; capturedUpdate = u; })
                .ReturnsAsync(new UpdateResult.Acknowledged(2, 2, null));

            (await Sut().RemoveRoleFromAllPermissionsAsync("manager", "acme")).Should().BeTrue();

            RenderFilter(capturedFilter).ToString().Should().Contain("\"Roles\" : \"manager\"").And.Contain("\"OrganizationId\" : \"acme\"");

            // Structural, so the assertion is about which field is pulled rather than about how
            // the driver happens to format the document: a $pull aimed at the wrong field would
            // acknowledge cleanly while leaving every reference in place.
            var update = RenderUpdate(capturedUpdate);
            update.Contains("$pull").Should().BeTrue();
            update["$pull"].AsBsonDocument["Roles"].AsString.Should().Be("manager");
        }

        [Fact]
        public async Task RemoveRoleFromAllUsersAsync_PullsFromOnlyThatOrganizationsBucket()
        {
            FilterDefinition<User>? capturedFilter = null;
            UpdateDefinition<User>? capturedUpdate = null;
            var col = Register<User>();
            col.Setup(c => c.UpdateManyAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<User>, UpdateDefinition<User>, UpdateOptions, CancellationToken>(
                    (f, u, _, _) => { capturedFilter = f; capturedUpdate = u; })
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            (await Sut().RemoveRoleFromAllUsersAsync("manager", "acme")).Should().BeTrue();

            // Section 8 asks for proof that a different organization's bucket is untouched. The
            // mock never mutates data, so the proof is that no definition mentions another bucket.
            var filter = RenderFilter(capturedFilter);
            var update = RenderUpdate(capturedUpdate);

            // Filter and update must address the SAME bucket and the same slug. A filter that
            // matched the right users while the update pulled from a different path would
            // acknowledge happily and change nothing.
            filter.ToString().Should().Contain("Roles.acme").And.Contain("manager").And.NotContain("Roles.globex");
            update["$pull"].AsBsonDocument["Roles.acme"].AsString.Should().Be("manager");
            update.ToString().Should().NotContain("Roles.globex");
        }

        [Fact]
        public async Task RemoveRoleFromAllUsersAsync_AcknowledgedWithNoMatches_IsStillSuccess()
        {
            var col = Register<User>();
            col.Setup(c => c.UpdateManyAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));

            // Acknowledged-with-zero is not a failure: a role nobody holds is the ordinary case,
            // and treating it as one would refuse to archive an unreferenced role.
            (await Sut().RemoveRoleFromAllUsersAsync("manager", "acme")).Should().BeTrue();
        }

        [Fact]
        public async Task RemoveRoleFromAllPermissionsAsync_AcknowledgedWithNoMatches_IsStillSuccess()
        {
            var col = Register<Permission>();
            col.Setup(c => c.UpdateManyAsync(It.IsAny<FilterDefinition<Permission>>(), It.IsAny<UpdateDefinition<Permission>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));

            (await Sut().RemoveRoleFromAllPermissionsAsync("manager", "acme")).Should().BeTrue();
        }

        [Theory]
        [InlineData("", "acme")]
        [InlineData("manager", "")]
        public async Task RoleArchiveHelpers_IgnoreBlankArguments(string slug, string org)
        {
            Register<Role>();
            Register<User>();
            Register<Permission>();

            (await Sut().HasChildRolesAsync(slug, org)).Should().BeFalse();
            (await Sut().HasUserAssignmentsAsync(slug, org)).Should().BeFalse();
            (await Sut().RemoveRoleFromAllPermissionsAsync(slug, org)).Should().BeTrue();
            (await Sut().RemoveRoleFromAllUsersAsync(slug, org)).Should().BeTrue();
        }

        // ---------- Direct permission-grant scrub (#465) ----------

        [Fact]
        public async Task RemovePermissionFromAllUsersAsync_PullsFromTheOrgBucketOnly()
        {
            FilterDefinition<User>? capturedFilter = null;
            UpdateDefinition<User>? capturedUpdate = null;
            var col = Register<User>();
            col.Setup(c => c.UpdateManyAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<User>, UpdateDefinition<User>, UpdateOptions, CancellationToken>((f, u, _, _) =>
                {
                    capturedFilter = f;
                    capturedUpdate = u;
                })
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            (await Sut().RemovePermissionFromAllUsersAsync("reports::export", "acme")).Should().BeTrue();

            var registry = BsonSerializer.SerializerRegistry;
            var filter = RenderFilter(capturedFilter).ToString();
            var update = capturedUpdate!.Render(new RenderArgs<User>(registry.GetSerializer<User>(), registry)).ToString();

            // Filter and update must name the SAME dotted path, and it must be Permissions.{org}
            // rather than Roles.{org} -- these are different grant dictionaries on the same
            // document, and only this one mints a permission claim.
            filter.Should().Contain("Permissions.acme").And.Contain("reports::export");
            update.Should().Contain("Permissions.acme").And.Contain("reports::export");
            // A user holding the same resource under a different organization key belongs to that
            // organization's copy and must be left alone.
            filter.Should().NotContain("Permissions.globex");
            update.Should().NotContain("Permissions.globex");
        }

        [Fact]
        public async Task RemovePermissionFromAllUsersAsync_MatchingNobodyIsSuccessNotFailure()
        {
            var col = Register<User>();
            col.Setup(c => c.UpdateManyAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<UpdateDefinition<User>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));

            // IsAcknowledged, not ModifiedCount: a permission nobody holds directly is a perfectly
            // normal archive, and treating it as a failure would abort the whole operation.
            (await Sut().RemovePermissionFromAllUsersAsync("reports::export", "acme")).Should().BeTrue();
        }

        [Theory]
        [InlineData("", "acme")]
        [InlineData("reports::export", "")]
        public async Task RemovePermissionFromAllUsersAsync_IgnoresBlankArguments(string resource, string org)
        {
            Register<User>();

            (await Sut().RemovePermissionFromAllUsersAsync(resource, org)).Should().BeTrue();
        }

        // ---------- Signup-default scrub ----------

        [Fact]
        public async Task RemoveRoleFromSignUpDefaultsAsync_PullsTheSlugFromTheRolesListOnly()
        {
            UpdateDefinition<TenantConfiguration>? capturedUpdate = null;
            var col = Register<TenantConfiguration>();
            col.Setup(c => c.UpdateOneAsync(It.IsAny<FilterDefinition<TenantConfiguration>>(), It.IsAny<UpdateDefinition<TenantConfiguration>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<TenantConfiguration>, UpdateDefinition<TenantConfiguration>, UpdateOptions, CancellationToken>((_, u, _, _) => capturedUpdate = u)
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            (await Sut().RemoveRoleFromSignUpDefaultsAsync("manager")).Should().BeTrue();

            var registry = BsonSerializer.SerializerRegistry;
            var update = capturedUpdate!.Render(new RenderArgs<TenantConfiguration>(registry.GetSerializer<TenantConfiguration>(), registry)).ToString();

            // The roles list, not the permissions list: they are separate signup defaults and a
            // slug must never be pulled from the one that holds permission resources.
            update.Should().Contain("DefaultRolesForNewUserOnSignUp").And.Contain("manager");
            update.Should().NotContain("DefaultPermissionsForNewUserOnSignUp");
        }

        [Fact]
        public async Task RemovePermissionFromSignUpDefaultsAsync_PullsTheResourceFromThePermissionsListOnly()
        {
            UpdateDefinition<TenantConfiguration>? capturedUpdate = null;
            var col = Register<TenantConfiguration>();
            col.Setup(c => c.UpdateOneAsync(It.IsAny<FilterDefinition<TenantConfiguration>>(), It.IsAny<UpdateDefinition<TenantConfiguration>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<TenantConfiguration>, UpdateDefinition<TenantConfiguration>, UpdateOptions, CancellationToken>((_, u, _, _) => capturedUpdate = u)
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            (await Sut().RemovePermissionFromSignUpDefaultsAsync("reports::export")).Should().BeTrue();

            var registry = BsonSerializer.SerializerRegistry;
            var update = capturedUpdate!.Render(new RenderArgs<TenantConfiguration>(registry.GetSerializer<TenantConfiguration>(), registry)).ToString();

            update.Should().Contain("DefaultPermissionsForNewUserOnSignUp").And.Contain("reports::export");
            update.Should().NotContain("DefaultRolesForNewUserOnSignUp");
        }

        [Fact]
        public async Task RemoveFromSignUpDefaultsAsync_MatchingNothingIsSuccessNotFailure()
        {
            var col = Register<TenantConfiguration>();
            col.Setup(c => c.UpdateOneAsync(It.IsAny<FilterDefinition<TenantConfiguration>>(), It.IsAny<UpdateDefinition<TenantConfiguration>>(), It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(0, 0, null));

            // A role that was never a signup default -- or a tenant with no configuration document
            // at all -- matches nothing. Treating that as a failure would abort every archive in
            // any tenant that has never opened Signup Configuration.
            (await Sut().RemoveRoleFromSignUpDefaultsAsync("manager")).Should().BeTrue();
            (await Sut().RemovePermissionFromSignUpDefaultsAsync("reports::export")).Should().BeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task RemoveFromSignUpDefaultsAsync_IgnoresBlankArguments(string value)
        {
            Register<TenantConfiguration>();

            (await Sut().RemoveRoleFromSignUpDefaultsAsync(value)).Should().BeTrue();
            (await Sut().RemovePermissionFromSignUpDefaultsAsync(value)).Should().BeTrue();
        }

        // ---------- Archive impact counting (#464) ----------

        [Fact]
        public async Task CountUsersWithRoleAsync_OrsEveryOrgBucketInOneQuery()
        {
            FilterDefinition<User>? captured = null;
            var col = Register<User>();
            col.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<User>, CountOptions, CancellationToken>((f, _, _) => captured = f)
                .ReturnsAsync(3);

            (await Sut().CountUsersWithRoleAsync("manager", new[] { "acme", "globex" }, activeOnly: false)).Should().Be(3);

            var rendered = RenderFilter(captured).ToString();
            // One query with an Or, not a count per organization: a user holding the slug in both
            // orgs must be counted once, and summing per-org counts would double them.
            rendered.Should().Contain("Roles.acme").And.Contain("Roles.globex").And.Contain("manager");
            rendered.Should().NotContain("\"Active\" : true");
        }

        [Fact]
        public async Task CountUsersWithRoleAsync_ActiveOnly_UsesTheSamePredicateAsTheArchiveGuard()
        {
            FilterDefinition<User>? captured = null;
            var col = Register<User>();
            col.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<User>, CountOptions, CancellationToken>((f, _, _) => captured = f)
                .ReturnsAsync(1);

            await Sut().CountUsersWithRoleAsync("manager", new[] { "acme" }, activeOnly: true);

            var rendered = RenderFilter(captured).ToString();
            rendered.Should().Contain("\"Active\" : true");
            // Missing Status counts as active, exactly as HasUserAssignmentsAsync treats it --
            // otherwise the preview would disagree with the guard it previews.
            rendered.Should().Contain("$exists");
        }

        [Fact]
        public async Task CountUsersWithPermissionAsync_ReadsTheDirectGrantDictionary()
        {
            FilterDefinition<User>? captured = null;
            var col = Register<User>();
            col.Setup(c => c.CountDocumentsAsync(It.IsAny<FilterDefinition<User>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<User>, CountOptions, CancellationToken>((f, _, _) => captured = f)
                .ReturnsAsync(2);

            (await Sut().CountUsersWithPermissionAsync("reports::export", new[] { "acme" })).Should().Be(2);

            var rendered = RenderFilter(captured).ToString();
            // Permissions.{org}, NOT Roles.{org}: this is the binding that mints a token claim.
            rendered.Should().Contain("Permissions.acme").And.Contain("reports::export");
            rendered.Should().NotContain("Roles.acme");
        }

        [Fact]
        public async Task GetNonArchivedRolesBySlugAsync_UsesNeTrueSoPreExistingRolesStillMatch()
        {
            FilterDefinition<Role>? captured = null;
            var col = Register<Role>(new List<Role> { new() { ItemId = "r1", Slug = "manager" } });
            col.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<Role>>(), It.IsAny<FindOptions<Role, Role>>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<Role>, FindOptions<Role, Role>, CancellationToken>((f, _, _) => captured = f)
                .ReturnsAsync(MongoMock.Cursor(new List<Role> { new() { ItemId = "r1", Slug = "manager" } }));

            await Sut().GetNonArchivedRolesBySlugAsync("manager");

            var rendered = RenderFilter(captured).ToString();
            // Ne(..., true) and never Eq(..., false): IsArchived is newer than the role documents,
            // so Eq would match zero pre-existing roles and report an empty blast radius.
            rendered.Should().Contain("$ne").And.Contain("IsArchived");
        }

        [Theory]
        [InlineData("", "acme")]
        [InlineData("manager", null)]
        public async Task ArchiveImpactCounters_ReturnZeroForBlankArguments(string slug, string? org)
        {
            Register<User>();
            Register<Permission>();
            Register<Role>();

            var orgs = org == null ? Array.Empty<string>() : new[] { org };

            (await Sut().CountUsersWithRoleAsync(slug, orgs, false)).Should().Be(0);
            (await Sut().CountUsersWithPermissionAsync(slug, orgs)).Should().Be(0);
            (await Sut().CountRoleBindingsForResourceAsync(slug, orgs)).Should().Be(0);
            (await Sut().GetNonArchivedRolesBySlugAsync(string.Empty)).Should().BeEmpty();
        }

        /// <summary>
        /// The tally behind Role.Count must ignore archived permissions.
        /// </summary>
        /// <remarks>
        /// Archiving a permission deliberately leaves its Roles array intact -- that array IS the
        /// binding, and pulling it would make the soft delete unrestorable -- so excluding archived
        /// documents from the count is the only thing that stops a role advertising a permission it
        /// no longer grants.
        ///
        /// Asserted on the rendered filter rather than on a tallied number, because the collection
        /// mock returns a fixed count and never evaluates the predicate: the query shape is the only
        /// thing this harness can actually observe.
        /// </remarks>
        [Fact]
        public async Task UpdateRolesCountAsync_ExcludesArchivedPermissionsFromTheTally()
        {
            FilterDefinition<Permission>? countFilter = null;
            var permissions = Register<Permission>();
            permissions.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Permission>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<Permission>, CountOptions, CancellationToken>((f, _, _) => countFilter = f)
                .ReturnsAsync(1);
            CaptureRoleUpdate(Register<Role>(), _ => { });

            await Sut().UpdateRolesCountAsync("admin", "default");

            var registry = BsonSerializer.SerializerRegistry;
            var rendered = countFilter!.Render(new RenderArgs<Permission>(registry.GetSerializer<Permission>(), registry));

            // Ne(true), not Eq(false): a document written before the field existed would not match
            // Eq(false) and would drop out of the count even though it is active.
            rendered["IsArchived"]["$ne"].AsBoolean.Should().BeTrue();
            // The scope of the tally is unchanged -- this role, in this organization.
            rendered["Roles"].AsString.Should().Be("admin");
            rendered["OrganizationId"].AsString.Should().Be("default");
        }
    }
}
