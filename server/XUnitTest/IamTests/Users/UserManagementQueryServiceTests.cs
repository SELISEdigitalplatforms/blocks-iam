using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Users
{
    public class UserManagementQueryServiceTests : IDisposable
    {
        private readonly Mock<IUserRepository> _repo = new();

        private UserManagementQueryService Create() =>
            new(NullLogger<UserManagementQueryService>.Instance, _repo.Object);

        private static void InstallContext(string userId = "user-1", string orgId = "default")
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: userId, impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        [Fact]
        public async Task IsUserAvailable_TrueWhenNoUser()
        {
            _repo.Setup(r => r.GetUserByEmailAsync("a@b.com")).ReturnsAsync((User)null!);
            var result = await Create().IsUserAvailableAsync(new IsEmailAvailableRequest { Email = "A@B.com" });
            result.Should().BeTrue();
        }

        [Fact]
        public async Task IsUserAvailable_FalseWhenUserExists()
        {
            _repo.Setup(r => r.GetUserByEmailAsync("a@b.com")).ReturnsAsync(new User { ItemId = "u1" });
            var result = await Create().IsUserAvailableAsync(new IsEmailAvailableRequest { Email = "A@B.com" });
            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsUserExist_ReturnsUserIdAndOrganizations()
        {
            _repo.Setup(r => r.GetUserByEmailAsync("a@b.com"))
                .ReturnsAsync(new User { ItemId = "u1", OrganizationIds = new List<string> { "org-1", "org-2" } });

            var result = await Create().IsUserExistAsync("A@B.com");

            result.UserId.Should().Be("u1");
            result.OrganizationIds.Should().BeEquivalentTo("org-1", "org-2");
        }

        [Fact]
        public async Task IsUserExist_ReturnsEmpty_WhenNoUser()
        {
            _repo.Setup(r => r.GetUserByEmailAsync("a@b.com")).ReturnsAsync((User)null!);

            var result = await Create().IsUserExistAsync("a@b.com");

            result.UserId.Should().BeNull();
            result.OrganizationIds.Should().BeEmpty();
        }

        [Fact]
        public async Task GetUsers_MapsDataAndReturnsCount()
        {
            var accounts = new List<GetAccounts>
            {
                new() { ItemId = "u1", Email = "u1@e.com", FirstName = "A" },
                new() { ItemId = "u2", Email = "u2@e.com", FirstName = "B" },
            }.AsQueryable();
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>()))
                .ReturnsAsync((accounts, 2L));

            var result = await Create().GetUsersAsync(new GetUsersRequest());

            result.TotalCount.Should().Be(2);
            result.Data.Should().HaveCount(2);
            result.Data.First()["itemId"].Should().Be("u1");
        }

        [Fact]
        public async Task GetUsers_HandlesNullData()
        {
            _repo.Setup(r => r.GetUsersAsync<GetAccounts, GetUsersRequest>(It.IsAny<GetUsersRequest>()))
                .ReturnsAsync(((IQueryable<GetAccounts>?)null, 0L));

            var result = await Create().GetUsersAsync(new GetUsersRequest());

            result.TotalCount.Should().Be(0);
            result.Data.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAccount_ReturnsMappedData_WhenUserFound()
        {
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("user-1"))
                .ReturnsAsync(new GetAccounts { ItemId = "user-1", Email = "u@e.com", OrganizationIds = new List<string> { "default" } });

            var result = await Create().GetAccountAsync();

            result.Data.Should().NotBeNull();
            result.Data!["itemId"].Should().Be("user-1");
        }

        [Fact]
        public async Task GetAccount_ReturnsNullData_WhenUserMissing()
        {
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("user-1")).ReturnsAsync((GetAccounts)null!);

            var result = await Create().GetAccountAsync();

            result.Data.Should().BeNull();
        }

        [Fact]
        public async Task GetUser_DefaultOrg_IncludesOrganizationsRolesAndPermissions()
        {
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u9"))
                .ReturnsAsync(new GetAccounts
                {
                    ItemId = "u9",
                    Email = "u9@e.com",
                    OrganizationIds = new List<string> { "default" },
                    Roles = new Dictionary<string, List<string>> { { "default", new List<string> { "admin" } } },
                    Permissions = new Dictionary<string, List<string>> { { "default", new List<string> { "read" } } }
                });

            var result = await Create().GetUserAsync("u9", "default");

            result.Data.Should().ContainKey("OrganizationsRoles");
            result.Data.Should().ContainKey("OrganizationsPermissions");
        }

        [Fact]
        public async Task GetUser_NonDefaultOrg_DoesNotIncludeOrganizationsRoles()
        {
            InstallContext();
            _repo.Setup(r => r.GetUserByIdAsync<GetAccounts>("u9"))
                .ReturnsAsync(new GetAccounts { ItemId = "u9", Email = "u9@e.com" });

            var result = await Create().GetUserAsync("u9", "org-42");

            result.Data.Should().NotContainKey("OrganizationsRoles");
        }
    }
}
