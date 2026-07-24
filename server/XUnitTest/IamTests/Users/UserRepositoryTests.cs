using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// Unit tests for <see cref="UserRepository"/>. Pure delegations are asserted against the mocked
    /// <see cref="IIdentityAccessManagementRepository"/>; the query methods that build filters and
    /// projections are exercised through mocked <see cref="IMongoCollection{T}"/> instances.
    /// </summary>
    public sealed class UserRepositoryTests : IDisposable
    {
        private readonly Mock<IIdentityAccessManagementRepository> _iam = new();

        public UserRepositoryTests()
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

        private UserRepository Sut() => new(_iam.Object);

        private Mock<IMongoCollection<T>> Register<T>(IEnumerable<T>? items = null)
        {
            var col = MongoMock.Collection(items);
            _iam.Setup(r => r.GetCollection<T>()).Returns(col.Object);
            return col;
        }

        [Fact]
        public async Task CheckPasswordBlackListedAsync_Delegates()
        {
            _iam.Setup(r => r.CheckPasswordBlackListedAsync("pw", "t1")).ReturnsAsync(true);
            (await Sut().CheckPasswordBlackListedAsync("pw", "t1")).Should().BeTrue();
        }

        [Fact]
        public async Task CreateUserAsync_NormalizesAndInserts()
        {
            var col = Register<User>();
            var user = new User { ItemId = "u1", Email = " USER@X.COM ", UserName = " User " };
            (await Sut().CreateUserAsync(user)).Should().BeTrue();
            user.Email.Should().Be("user@x.com");
            user.UserName.Should().Be("user");
            col.Verify(c => c.InsertOneAsync(It.IsAny<User>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetIamConfigurationAsync_Delegates()
        {
            var cfg = new IamConfiguration { ItemId = ObjectId.GenerateNewId() };
            _iam.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(cfg);
            (await Sut().GetIamConfigurationAsync()).Should().BeSameAs(cfg);
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ById_NoUser_ReturnsEmpty()
        {
            Register<User>();
            (await Sut().GetPermissionsByResourcesAsync("missing")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ById_NoPermissions_ReturnsEmpty()
        {
            Register(new[] { new User { ItemId = "u1" } });
            (await Sut().GetPermissionsByResourcesAsync("u1")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ById_ReturnsProjectedPermissions()
        {
            Register(new[] { new User { ItemId = "u1", Permissions = new() { { "default", new List<string> { "res1" } } } } });
            var perms = Register<Permission>();
            MongoMock.SetupProjectedFind(perms, new List<GetUserPermission>
            {
                new() { ItemId = "p1", Resource = "res1", Name = "P1" }
            });
            var result = await Sut().GetPermissionsByResourcesAsync("u1");
            result.Should().ContainSingle(p => p.ItemId == "p1");
        }

        [Fact]
        public async Task GetPermissionsByResourcesAsync_ByList_ReturnsProjected()
        {
            var perms = Register<Permission>();
            MongoMock.SetupProjectedFind(perms, new List<GetUserPermission> { new() { ItemId = "p1", Resource = "res1" } });
            (await Sut().GetPermissionsByResourcesAsync(new List<string> { "res1" })).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetPermissionsByRolesAsync_Empty_ReturnsEmpty()
        {
            (await Sut().GetPermissionsByRolesAsync(new List<string>())).Should().BeEmpty();
        }

        [Fact]
        public async Task GetPermissionsByRolesAsync_ReturnsProjected()
        {
            var perms = Register<Permission>();
            MongoMock.SetupProjectedFind(perms, new List<GetUserPermission> { new() { ItemId = "p1" } });
            (await Sut().GetPermissionsByRolesAsync(new List<string> { "admin" })).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRolesBySlugsAsync_ById_NoUser_ReturnsEmpty()
        {
            Register<User>();
            (await Sut().GetRolesBySlugsAsync("missing")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetRolesBySlugsAsync_ById_ReturnsProjectedRoles()
        {
            Register(new[] { new User { ItemId = "u1", Roles = new() { { "default", new List<string> { "admin" } } } } });
            var roles = Register<Role>();
            MongoMock.SetupProjectedFind(roles, new List<GetUserRole> { new() { ItemId = "r1", Slug = "admin", Name = "Admin" } });
            (await Sut().GetRolesBySlugsAsync("u1")).Should().ContainSingle(r => r.Slug == "admin");
        }

        [Fact]
        public async Task GetRolesBySlugsAsync_ByList_ReturnsProjected()
        {
            var roles = Register<Role>();
            MongoMock.SetupProjectedFind(roles, new List<GetUserRole> { new() { ItemId = "r1", Slug = "admin" } });
            (await Sut().GetRolesBySlugsAsync(new List<string> { "admin" })).Should().HaveCount(1);
        }

        [Fact]
        public async Task GetUserByEmailAsync_Delegates_WithNormalizedEmail()
        {
            var user = new User { ItemId = "u1" };
            _iam.Setup(r => r.GetUserByEmailAsync("user@x.com")).ReturnsAsync(user);
            (await Sut().GetUserByEmailAsync(" USER@X.COM ")).Should().BeSameAs(user);
        }

        [Fact]
        public async Task GetUserByIdAsync_Delegates()
        {
            var user = new User { ItemId = "u1" };
            _iam.Setup(r => r.GetUserByIdAsync("u1")).ReturnsAsync(user);
            (await Sut().GetUserByIdAsync("u1")).Should().BeSameAs(user);
        }

        [Fact]
        public async Task GetUserByIdAsyncGeneric_Delegates()
        {
            _iam.Setup(r => r.GetUserByIdAsync<string>("u1")).ReturnsAsync("value");
            (await Sut().GetUserByIdAsync<string>("u1")).Should().Be("value");
        }

        [Fact]
        public async Task GetUserByUserNameOrgIdAsync_WithOrg_ReturnsMatch()
        {
            Register(new[] { new User { ItemId = "u1", UserName = "user", OrganizationIds = new List<string> { "org1" } } });
            (await Sut().GetUserByUserNameOrgIdAsync("USER", "org1"))!.ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task GetUserByUserNameOrgIdAsync_WithoutOrg_ReturnsMatch()
        {
            Register(new[] { new User { ItemId = "u1", UserName = "user" } });
            (await Sut().GetUserByUserNameOrgIdAsync("user"))!.ItemId.Should().Be("u1");
        }

        [Fact]
        public async Task GetUsersAsync_ProjectsAndReturnsCount()
        {
            var col = Register(new[] { new User { ItemId = "u1" }, new User { ItemId = "u2" } });
            MongoMock.SetupCount(col, 2);
            var query = new BaseGetsRequest<GetUsersFilter>
            {
                Page = 0,
                PageSize = 10,
                Sort = new BaseSortRequest { Property = "Email", IsDescending = false },
                Filter = new GetUsersFilter
                {
                    Name = "john",
                    Email = "john@x.com",
                    Status = new Status { Active = true },
                    Mfa = new MFA { Enabled = true },
                    JoinedOn = DateTime.UtcNow.AddDays(-10),
                    LastLogin = DateTime.UtcNow.AddDays(-1),
                    UserIds = new List<string> { "u1" },
                    OrganizationId = "org1"
                }
            };
            var (items, count) = await Sut().GetUsersAsync<User, BaseGetsRequest<GetUsersFilter>>(query);
            count.Should().Be(2);
            items!.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetUsersAsync_NullFilter_UsesDefaults()
        {
            var col = Register(new[] { new User { ItemId = "u1" } });
            MongoMock.SetupCount(col, 1);
            var (items, count) = await Sut().GetUsersAsync<User, BaseGetsRequest<GetUsersFilter>>(
                new BaseGetsRequest<GetUsersFilter>());
            count.Should().Be(1);
            items!.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetUsersAsync_InactiveAndMfaDisabledFilters()
        {
            var col = Register(new[] { new User { ItemId = "u1" } });
            MongoMock.SetupCount(col, 1);
            var query = new BaseGetsRequest<GetUsersFilter>
            {
                Filter = new GetUsersFilter
                {
                    Status = new Status { Inactive = true },
                    Mfa = new MFA { Disabled = true }
                }
            };
            var (_, count) = await Sut().GetUsersAsync<User, BaseGetsRequest<GetUsersFilter>>(query);
            count.Should().Be(1);
        }

        [Fact]
        public async Task InsertUserKeyMapAsync_Delegates()
        {
            _iam.Setup(r => r.InsertUserKeyMapAsync(It.IsAny<UserKeyMap>())).ReturnsAsync(true);
            (await Sut().InsertUserKeyMapAsync(new UserKeyMap { ItemId = "k1" })).Should().BeTrue();
        }

        [Fact]
        public async Task UpdateUserAsync_NormalizesAndDelegates()
        {
            User? captured = null;
            _iam.Setup(r => r.UpdateUserAsync(It.IsAny<User>()))
                .Callback<User>(u => captured = u).ReturnsAsync(true);
            (await Sut().UpdateUserAsync(new User { ItemId = "u1", Email = " A@B.COM ", UserName = " Bob " })).Should().BeTrue();
            captured!.Email.Should().Be("a@b.com");
            captured.UserName.Should().Be("bob");
        }

        [Fact]
        public async Task GetProjectIdFromProjectPeopleAsync_ReturnsTenantId()
        {
            Register(new[] { new ProjectPeople { ItemId = "pp1", UserId = "u1", TenantId = "tenant-9" } });
            (await Sut().GetProjectIdFromProjectPeopleAsync("u1")).Should().Be("tenant-9");
        }
    }
}
