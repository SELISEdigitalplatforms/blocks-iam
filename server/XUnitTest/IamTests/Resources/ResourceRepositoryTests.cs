using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using MongoDB.Bson;
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
        /// The usage count now comes from a server-side <c>CountDocumentsAsync</c> over a typed
        /// <see cref="Permission"/> collection, so the permissions registered here are what the count
        /// reflects. <see cref="MongoMock.Collection{T}"/> answers the count with the list length.
        /// </summary>
        private Mock<IMongoCollection<Permission>> RegisterPermissionsNamed(params Permission[] permissions)
        {
            var col = MongoMock.Collection(permissions.AsEnumerable());
            _iam.Setup(r => r.GetCollectionByName<Permission>("Permissions")).Returns(col.Object);
            return col;
        }

        [Fact]
        public async Task UpdateRolesCountAsync_CountsRoleUsageAndWritesItToTheRole()
        {
            var permissions = RegisterPermissionsNamed(
                Perm("p1", roles: new[] { "admin" }),
                Perm("p2", roles: new[] { "admin" }));
            var roles = Register<Role>();

            (await Sut().UpdateRolesCountAsync("admin", "default")).Should().BeTrue();

            permissions.Verify(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<Permission>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
            roles.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<Role>>(), It.IsAny<UpdateDefinition<Role>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdateRolesCountAsync_CountsZeroWhenNoPermissionReferencesTheRole()
        {
            RegisterPermissionsNamed();
            var roles = Register<Role>();

            (await Sut().UpdateRolesCountAsync("admin", "default")).Should().BeTrue();

            // The role is still written, so a role that lost its last permission is reset to 0
            // rather than keeping a stale count.
            roles.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<Role>>(), It.IsAny<UpdateDefinition<Role>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdateRolesCountAsync_ScopesTheCountToTheGivenOrganization()
        {
            // The count filter pairs the role slug with the organization, so an explicit org id has
            // to reach the query rather than falling back to the ambient one.
            var permissions = RegisterPermissionsNamed(Perm("p1", org: "org1", roles: new[] { "admin" }));
            Register<Role>();

            (await Sut().UpdateRolesCountAsync("admin", "org1")).Should().BeTrue();

            permissions.Verify(c => c.CountDocumentsAsync(
                It.IsAny<FilterDefinition<Permission>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdateRolesCountAsync_ReturnsFalseWhenTheRoleUpdateIsNotAcknowledged()
        {
            RegisterPermissionsNamed(Perm("p1", roles: new[] { "admin" }));
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
    }
}
