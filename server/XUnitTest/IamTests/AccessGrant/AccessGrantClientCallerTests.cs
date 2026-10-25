using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Utilities;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.AccessGrant
{
    /// <summary>
    /// A client-credentials token (a client id, no user) outside the <c>default</c> organization
    /// is not the system user: it holds only its own client record's power, over the standard tree.
    /// </summary>
    public sealed class AccessGrantClientCallerTests : IDisposable
    {
        private const string Org = AccessGrantTestKit.Org;
        private const string ClientId = "svc-1";

        private readonly AccessGrantTestKit _kit = new AccessGrantTestKit().WithStandardTree();

        public AccessGrantClientCallerTests()
        {
            _kit.WithClient(ClientId, ["manager"], ["mutate-users"]);
            AccessGrantTestKit.SignInClient(ClientId);
        }

        public void Dispose() => AccessGrantTestKit.SignOut();

        // ---------- who the caller is ----------

        [Fact]
        public void ClientOfANonDefaultOrganizationIsNotTheSystemUser()
        {
            var caller = _kit.Service().CurrentCaller();

            caller.IsSystem.Should().BeFalse();
            caller.IsClient.Should().BeTrue();
            caller.UserId.Should().BeEmpty();
            caller.ClientId.Should().Be(ClientId);
            caller.CallerId.Should().Be("client:" + ClientId);
            caller.OrganizationId.Should().Be(Org);
        }

        [Theory]
        [InlineData("default")]
        [InlineData("")]
        [InlineData(null)]
        public void ClientOfTheDefaultOrganizationIsTheSystemUser(string? org)
        {
            AccessGrantTestKit.SignInClient(ClientId, org);

            var caller = _kit.Service().CurrentCaller();

            caller.IsSystem.Should().BeTrue();
            caller.OrganizationId.Should().Be(IdpConstants.DefaultOrganizationId);
        }

        [Fact]
        public void UnauthenticatedClientIsTheSystemUser()
        {
            AccessGrantTestKit.SignInClient(ClientId, authenticated: false);

            _kit.Service().CurrentCaller().IsSystem.Should().BeTrue();
        }

        [Fact]
        public void NoUserAndNoClientIsTheSystemUser()
        {
            AccessGrantTestKit.SignInClient(string.Empty);

            var caller = _kit.Service().CurrentCaller();

            caller.IsSystem.Should().BeTrue();
            caller.IsClient.Should().BeFalse();
        }

        [Fact]
        public void ImpersonatedCallWithAClientIdActsAsTheClient()
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: AccessGrantTestKit.Tenant, roles: null, userId: null, isAuthenticated: true,
                requestUri: "https://test", organizationId: Org, expireOn: DateTime.UtcNow.AddHours(1),
                email: null, permissions: null, userName: null, phoneNumber: null, displayName: null,
                oauthToken: null, originalTenantId: AccessGrantTestKit.Tenant, impersonated: true, clientId: ClientId));

            var caller = _kit.Service().CurrentCaller();
            caller.IsSystem.Should().BeFalse();
            caller.IsClient.Should().BeTrue();
        }

        // ---------- power comes from the client record ----------

        [Fact]
        public async Task ScopeIsTheClientRecordsPowerAndReadsNoUser()
        {
            var scope = await _kit.Service().GetScopeAsync();

            scope.IsSystem.Should().BeFalse();
            scope.HeldRoles.Should().BeEquivalentTo("manager");
            scope.ManagedRoles.Should().BeEquivalentTo("viewer");
            scope.HeldPermissions.Should().BeEquivalentTo(
                "read", "write", "mutate-users", AccessGrantRules.MutateRolesPermission, AccessGrantRules.MutateUsersPermission);
            scope.IsTop.Should().BeFalse("the client's manage power comes from manager, which has a parent");
            _kit.Repository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("viewer", true)]   // below the client's role
        [InlineData("manager", false)] // held, but needs a role above it
        [InlineData("admin", false)]   // above the client's tree
        [InlineData("owner", false)]   // outside the tree, carries billing
        public async Task ClientAssignsOnlyRolesItManages(string slug, bool allowed)
        {
            var service = _kit.Service();

            (await service.AuthorizeCurrentCallerAsync(scope => AccessGrantRules.CheckAssignRole(scope, slug)))
                .Should().Be(allowed);
        }

        [Fact]
        public async Task ADescendantClientGivesRolesButNoDirectPermissions()
        {
            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            (await service.AuthorizeGrantAsync(scope, ["viewer"], [])).Should().BeTrue();
            (await service.AuthorizeGrantAsync(scope, [], ["write"])).Should().BeFalse();
            _kit.Logged($"AccessGrant denied R2-GrantPermission client:{ClientId} {Org} write descendant_cannot_grant_direct_permission")
                .Should().BeTrue();
        }

        [Fact]
        public async Task AClientWithAStandaloneManageRoleIsATopUser()
        {
            _kit.WithClient(ClientId, ["admin"]);
            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            scope.IsTop.Should().BeTrue();
            (await service.AuthorizeGrantAsync(scope, ["owner"], ["billing"])).Should().BeTrue();
        }

        [Fact]
        public async Task ClientCannotActOnAnotherOrganizationsUser()
        {
            _kit.WithUser("outsider", ["viewer"], organizationId: "org-other");

            (await _kit.Service().AuthorizeManageUserAsync("outsider")).Should().BeFalse();
            _kit.Logged("user_not_member_of_organization").Should().BeTrue();
        }

        [Fact]
        public async Task ClientCanManageAUserBelowIt()
        {
            _kit.WithUser("junior", ["viewer"]);
            _kit.WithUser("boss", ["admin"]);
            var service = _kit.Service();

            (await service.AuthorizeManageUserAsync("junior")).Should().BeTrue();
            (await service.AuthorizeManageUserAsync("boss")).Should().BeFalse();
        }

        // ---------- a client that grants nothing ----------

        [Theory]
        [InlineData("missing")]
        [InlineData("inactive")]
        [InlineData("other-organization")]
        public async Task UnusableClientHoldsNothing(string state)
        {
            switch (state)
            {
                case "missing":
                    AccessGrantTestKit.SignInClient("svc-gone");
                    break;
                case "inactive":
                    _kit.WithClient(ClientId, ["admin"], ["mutate-users"], isActive: false);
                    break;
                default:
                    _kit.WithStandardTree("org-other");
                    _kit.WithClient(ClientId, ["admin"], ["mutate-users"], organizationId: "org-other");
                    break;
            }

            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            scope.IsSystem.Should().BeFalse();
            scope.HeldRoles.Should().BeEmpty();
            scope.HeldPermissions.Should().BeEmpty();
            (await service.AuthorizeGrantAsync(scope, ["viewer"], [])).Should().BeFalse();
            (await service.AuthorizeGrantAsync(scope, [], ["mutate-users"])).Should().BeFalse();
        }

        // ---------- the self rule ----------

        [Fact]
        public async Task ClientCallerIdNeverMatchesAUser()
        {
            _kit.WithUser(ClientId, ["viewer"]);
            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            scope.CallerId.Should().NotBe(ClientId);
            AccessGrantRules.CheckManageUser(scope, _kit.FindUser(ClientId)!).Should().BeNull();
        }
    }
}
