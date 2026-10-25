using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.AccessGrant
{
    /// <summary>
    /// The service around the rules: who the system user is, power read from the database, what
    /// a would-deny does, the policy-power warning (R11), ending sessions after
    /// a removal (R12), the assignable-roles picker and the grant-scope read.
    /// </summary>
    public sealed class AccessGrantServiceTests : IDisposable
    {
        private const string Org = AccessGrantTestKit.Org;

        private readonly AccessGrantTestKit _kit = new AccessGrantTestKit().WithStandardTree();

        public AccessGrantServiceTests()
        {
            _kit.WithUser("caller", ["manager"]);
            AccessGrantTestKit.SignIn("caller");
        }

        public void Dispose() => AccessGrantTestKit.SignOut();

        // ---------- the system user ----------

        [Theory]
        [InlineData("default", true, false, true)]
        [InlineData("", true, false, true)]
        [InlineData(null, true, false, true)]
        [InlineData(Org, true, true, true)]   // impersonated platform call
        [InlineData(Org, false, false, true)] // no signed-in user
        [InlineData(Org, true, false, false)]
        public void SystemUserIsDefaultOrganizationNoUserOrImpersonated(string? org, bool authenticated, bool impersonated, bool isSystem)
        {
            AccessGrantTestKit.SignIn("caller", org, impersonated, authenticated);

            _kit.Service().CurrentCaller().IsSystem.Should().Be(isSystem);
        }

        [Fact]
        public void NoContextIsTheSystemUser()
        {
            BlocksContext.SetContext(null);

            var caller = _kit.Service().CurrentCaller();

            caller.IsSystem.Should().BeTrue();
            caller.OrganizationId.Should().Be(IdpConstants.DefaultOrganizationId);
        }

        [Fact]
        public void RootTenantCallIsTheSystemUser()
        {
            _kit.Tenants.Setup(t => t.GetTenantByID(AccessGrantTestKit.Tenant)).Returns(new Tenant
            {
                TenantId = AccessGrantTestKit.Tenant,
                IsRootTenant = true,
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
            });

            _kit.Service().CurrentCaller().IsSystem.Should().BeTrue();
        }

        // ---------- the OS console caller ----------

        [Theory]
        [InlineData(Org)]
        [InlineData("default")] // a single-organization project's own token is still not the console
        public void ProjectTokenIsNotTheConsole(string org)
        {
            AccessGrantTestKit.SignIn("caller", org);

            _kit.Service().IsConsoleCaller().Should().BeFalse();
        }

        [Fact]
        public void ImpersonatedTokenIsTheConsole()
        {
            AccessGrantTestKit.SignIn("caller", Org, impersonated: true);

            _kit.Service().IsConsoleCaller().Should().BeTrue();
        }

        [Fact]
        public void RootTenantTokenIsTheConsole()
        {
            _kit.Tenants.Setup(t => t.GetTenantByID(AccessGrantTestKit.Tenant)).Returns(new Tenant
            {
                TenantId = AccessGrantTestKit.Tenant,
                IsRootTenant = true,
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow }
            });

            _kit.Service().IsConsoleCaller().Should().BeTrue();
        }

        [Fact]
        public void UnauthenticatedOrMissingContextIsNotTheConsole()
        {
            AccessGrantTestKit.SignIn("caller", Org, impersonated: true, authenticated: false);
            _kit.Service().IsConsoleCaller().Should().BeFalse();

            BlocksContext.SetContext(null);
            _kit.Service().IsConsoleCaller().Should().BeFalse();
        }

        [Fact]
        public void ProjectClientCredentialIsNotTheConsole()
        {
            AccessGrantTestKit.SignInClient("client-1");

            _kit.Service().IsConsoleCaller().Should().BeFalse();
        }

        [Fact]
        public async Task SystemUserScopeMakesNoReads()
        {
            AccessGrantTestKit.SignIn("caller", "default");

            var scope = await _kit.Service().GetScopeAsync();

            scope.IsSystem.Should().BeTrue();
            _kit.Repository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);
            _kit.Repository.Verify(r => r.GetRolesAsync(It.IsAny<string>()), Times.Never);
        }

        // ---------- power comes from the database, not the token ----------

        [Fact]
        public async Task ScopeIsReadFromTheDatabaseAndIgnoresTokenRoles()
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: AccessGrantTestKit.Tenant, roles: ["admin", "owner"], userId: "caller", isAuthenticated: true,
                requestUri: "https://test", organizationId: Org, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                permissions: ["billing"], userName: "t", phoneNumber: null, displayName: "T", oauthToken: null, originalTenantId: AccessGrantTestKit.Tenant));

            var scope = await _kit.Service().GetScopeAsync();

            scope.HeldRoles.Should().BeEquivalentTo("manager");
            scope.HeldPermissions.Should().NotContain("billing");
        }

        // ---------- Refusal ----------

        [Fact]
        public async Task RefusesAndLogsTheReason()
        {
            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            service.Authorize(scope, AccessGrantRules.CheckAssignRole(scope, "owner")).Should().BeFalse();
            _kit.Logged($"AccessGrant denied R1-AssignRole caller {Org} owner role_carries_permission_not_held").Should().BeTrue();
        }

        [Fact]
        public async Task APassIsSilent()
        {
            var service = _kit.Service();

            (await service.AuthorizeCurrentCallerAsync(scope => AccessGrantRules.CheckAssignRole(scope, "viewer"))).Should().BeTrue();
            _kit.Logger.Messages.Should().BeEmpty();
        }

        [Fact]
        public void SystemOnlyNeedsNoReads()
        {
            var service = _kit.Service();

            service.AuthorizeSystemOnly("organizations/config").Should().BeFalse();
            _kit.Repository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);

            AccessGrantTestKit.SignIn("caller", "default");
            service.AuthorizeSystemOnly("organizations/config").Should().BeTrue();
        }

        // ---------- R3 by id ----------

        [Fact]
        public async Task ManageUserByIdLoadsTheTarget()
        {
            _kit.WithUser("boss", ["owner"]);
            _kit.WithUser("junior", ["viewer"]);
            var service = _kit.Service();

            (await service.AuthorizeManageUserAsync("boss")).Should().BeFalse();
            (await service.AuthorizeManageUserAsync("junior")).Should().BeTrue();
            (await service.AuthorizeManageUserAsync("missing")).Should().BeTrue("a missing user is answered by the path itself");
        }

        // ---------- R11 ----------

        [Fact]
        public async Task GrantingAPolicyRoleTheCallerDoesNotHoldOrManageWarns()
        {
            _kit.PolicyRoles.Add("auditor");
            _kit.PolicyRoles.Add("viewer");
            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            (await service.AuthorizeGrantAsync(scope, ["auditor", "viewer"], [])).Should().BeTrue();

            _kit.Logged("AccessGrant.RoleCarriesPolicyPower auditor").Should().BeTrue();
            _kit.Logged("AccessGrant.RoleCarriesPolicyPower viewer").Should().BeFalse("viewer is below the caller's manager role");
        }

        [Fact]
        public async Task PolicyStoreFailureNeverFailsTheGrant()
        {
            _kit.Repository.Setup(r => r.GetPolicyRoleSlugsAsync()).ThrowsAsync(new InvalidOperationException("down"));
            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            (await service.AuthorizeGrantAsync(scope, ["auditor"], [])).Should().BeTrue();
            _kit.Logged("could not read data-access policies").Should().BeTrue();
        }

        // ---------- R12 ----------

        [Fact]
        public async Task EndsEverySessionOfUsersWhoLostAccess()
        {
            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            await service.EndSessionsAfterRemovalAsync(scope, ["u1", "u2", "u1", " "], "access_replaced");

            _kit.Terminator.Verify(t => t.EndAllSessionsAsync("u1", "access_replaced"), Times.Once);
            _kit.Terminator.Verify(t => t.EndAllSessionsAsync("u2", "access_replaced"), Times.Once);
        }

        [Fact]
        public async Task SessionEndFallsBackToTheLogoutAllQueue()
        {
            _kit.Terminator.Setup(t => t.EndAllSessionsAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));
            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            await service.EndSessionsAfterRemovalAsync(scope, ["u1"], "access_revoked");

            _kit.Iam.Verify(i => i.SendToQueueAsync(IdpConstants.AuthenticationQueue,
                It.Is<LogoutAllEvent>(e => e.UserId == "u1")), Times.Once);
        }

        [Fact]
        public async Task SystemUserRemovalsEndNoSessions()
        {
            AccessGrantTestKit.SignIn("caller", "default");
            var service = _kit.Service();
            var scope = await service.GetScopeAsync();

            await service.EndSessionsAfterRemovalAsync(scope, ["u1"], "access_replaced");
            (await service.GetRoleHoldersAsync(scope, "viewer")).Should().BeEmpty();

            _kit.Terminator.Verify(t => t.EndAllSessionsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- GET roles/assignable and GET iam/grant-scope ----------

        [Fact]
        public async Task AssignableRolesAreTheOnesThatPassR1()
        {
            var result = await _kit.Service().GetAssignableRolesAsync();

            result.Hierarchy.Select(r => r.Slug).Concat(result.Standalone.Select(r => r.Slug))
                .Should().BeEquivalentTo("viewer", "auditor");
        }

        [Fact]
        public async Task GrantScopeDescribesTheCaller()
        {
            var result = await _kit.Service().GetGrantScopeAsync();

            result.IsSystemUser.Should().BeFalse();
            result.HeldRoles.Should().Equal("manager");
            result.ManagedRoles.Should().Equal("viewer");
            result.AssignableRoles.Should().Equal("auditor", "viewer");
            result.HeldPermissions.Should().Equal("read", "write");
        }
    }
}
