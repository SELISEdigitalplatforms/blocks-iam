using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// The role filter against a real Mongo: users are stored with roles keyed by organization
    /// (<c>Roles.{organizationId}</c>), exactly as an organization invite writes them.
    /// </summary>
    public sealed class UserRepositoryRoleFilterRealMongoTests : IDisposable
    {
        private const string OrgA = "3f1c2a7e-0b8d-4c1e-9a55-0d7c5e2b1a10";
        private const string OrgB = "8a2b4c6d-1e3f-4a5b-8c7d-9e0f1a2b3c4d";
        private const string Admin = "admin";

        private readonly RealMongo _mongo = new();
        private readonly Mock<IIdentityAccessManagementRepository> _iam = new();

        public UserRepositoryRoleFilterRealMongoTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            _iam.Setup(r => r.GetCollection<User>()).Returns(_mongo.Database.GetCollection<User>("Users"));
        }

        public void Dispose()
        {
            _mongo.Dispose();
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private UserRepository Sut() => new(_iam.Object, new XUnitTest.TestDoubles.PassThroughIamDataAccess());

        private async Task SeedAsync()
        {
            var users = _mongo.Database.GetCollection<User>("Users");
            await users.InsertManyAsync(
            [
                new User
                {
                    ItemId = "in-a-as-admin",
                    Email = "a@x.com",
                    OrganizationIds = [IdpConstants.DefaultOrganizationId, OrgA],
                    Roles = new() { [IdpConstants.DefaultOrganizationId] = ["viewer"], [OrgA] = [Admin] }
                },
                new User
                {
                    ItemId = "in-a-as-viewer",
                    Email = "b@x.com",
                    OrganizationIds = [OrgA],
                    Roles = new() { [OrgA] = ["viewer"] }
                },
                new User
                {
                    ItemId = "admin-elsewhere",
                    Email = "c@x.com",
                    OrganizationIds = [OrgA, OrgB],
                    Roles = new() { [OrgA] = ["viewer"], [OrgB] = [Admin] }
                }
            ]);
        }

        private async Task<List<string>> QueryAsync(UserListScope scope, params string[] roles)
        {
            var (data, count) = await Sut().GetUsersAsync<User, GetUsersRequest>(
                new GetUsersRequest { Page = 0, PageSize = 50, Filter = new GetUsersFilter { Roles = [.. roles] } },
                scope);

            var ids = data!.Select(u => u.ItemId).OrderBy(x => x, StringComparer.Ordinal).ToList();
            count.Should().Be(ids.Count);
            return ids;
        }

        [RealMongoFact]
        public async Task RoleFilter_MatchesOnlyTheRoleHeldInTheRequestedOrganization()
        {
            await SeedAsync();

            var ids = await QueryAsync(new UserListScope(UserListScopeKind.Organizations, [OrgA]), Admin);

            ids.Should().Equal("in-a-as-admin");
        }

        [RealMongoFact]
        public async Task RoleFilter_AnyOfSeveralRoles()
        {
            await SeedAsync();

            var ids = await QueryAsync(new UserListScope(UserListScopeKind.Organizations, [OrgA]), Admin, "viewer");

            ids.Should().Equal("admin-elsewhere", "in-a-as-admin", "in-a-as-viewer");
        }
    }
}
