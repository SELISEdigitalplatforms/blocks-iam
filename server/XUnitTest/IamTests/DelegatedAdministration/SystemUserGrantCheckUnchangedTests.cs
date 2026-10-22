using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Resources;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.DelegatedAdministration
{
    /// <summary>
    /// The system user -- every caller of a single-organization tenant -- keeps the assignable-roles
    /// picker it had before delegated administration: the CanCreateOwn trees of its token roles plus
    /// standalone roles. These are the pre-existing ResourceQueryService cases, run through the new
    /// service, with the same expected outputs.
    /// </summary>
    public sealed class SystemUserGrantCheckUnchangedTests : IDisposable
    {
        private readonly DelegationTestKit _kit = new();

        public void Dispose() => DelegationTestKit.SignOut();

        private static void SignInDefault(IEnumerable<string> tokenRoles)
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: DelegationTestKit.Tenant, roles: tokenRoles, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: DelegationTestKit.Tenant, impersonationSessionId: string.Empty, applicationDomain: "test"));
        }

        private void Roles(params Role[] roles) =>
            _kit.LegacyResources.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((roles.AsQueryable(), roles.Length));

        [Fact]
        public async Task ClassifiesHierarchyAndStandaloneExactlyAsBefore()
        {
            SignInDefault(["manager"]);
            Roles(
                new Role { Slug = "manager", Name = "Manager", CanCreateOwn = true, AncestorRoleSlugs = [] },
                new Role { Slug = "team-lead", Name = "Team Lead", CanCreateOwn = true, ParentRoleSlug = "manager", AncestorRoleSlugs = ["manager"] },
                new Role { Slug = "admin", Name = "Admin", CanCreateOwn = true, AncestorRoleSlugs = [] },
                new Role { Slug = "guest", Name = "Guest", CanCreateOwn = false, AncestorRoleSlugs = [] });

            var result = await _kit.Service().GetAssignableRolesAsync();

            result.Hierarchy.Select(x => x.Slug).Should().BeEquivalentTo("manager", "team-lead");
            result.Standalone.Select(x => x.Slug).Should().BeEquivalentTo("guest");
            _kit.Repository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RequestsTheSameLargePage()
        {
            SignInDefault(["manager"]);

            await _kit.Service().GetAssignableRolesAsync();

            _kit.LegacyResources.Verify(r => r.GetRolesAsync(It.Is<GetRolesRequest>(q => q.PageSize == 1000), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task NoTokenRolesYieldsOnlyStandalone()
        {
            SignInDefault([]);
            Roles(
                new Role { Slug = "manager", Name = "Manager", CanCreateOwn = true, AncestorRoleSlugs = [] },
                new Role { Slug = "guest", Name = "Guest", CanCreateOwn = false, AncestorRoleSlugs = [] });

            var result = await _kit.Service().GetAssignableRolesAsync();

            result.Hierarchy.Should().BeEmpty();
            result.Standalone.Select(x => x.Slug).Should().BeEquivalentTo("guest");
        }

        [Fact]
        public async Task MatchesTokenRolesCaseInsensitively()
        {
            SignInDefault(["MANAGER"]);
            Roles(new Role { Slug = "manager", Name = "Manager", CanCreateOwn = true, AncestorRoleSlugs = [] });

            var result = await _kit.Service().GetAssignableRolesAsync();

            result.Hierarchy.Select(x => x.Slug).Should().BeEquivalentTo("manager");
        }

        [Fact]
        public async Task RoleReferencedAsAncestorIsNotStandalone()
        {
            SignInDefault([]);
            Roles(
                new Role { Slug = "root", Name = "Root", CanCreateOwn = false, AncestorRoleSlugs = [] },
                new Role { Slug = "child", Name = "Child", CanCreateOwn = true, ParentRoleSlug = "root", AncestorRoleSlugs = ["root"] });

            var result = await _kit.Service().GetAssignableRolesAsync();

            result.Standalone.Should().BeEmpty();
            result.Hierarchy.Should().BeEmpty();
        }

        [Fact]
        public async Task DelegationScopeOfTheSystemUserShowsTheSamePicker()
        {
            SignInDefault(["manager"]);
            Roles(
                new Role { Slug = "manager", Name = "Manager", CanCreateOwn = true, AncestorRoleSlugs = [] },
                new Role { Slug = "admin", Name = "Admin", CanCreateOwn = true, AncestorRoleSlugs = [] },
                new Role { Slug = "guest", Name = "Guest", CanCreateOwn = false, AncestorRoleSlugs = [] });

            var result = await _kit.Service().GetDelegationScopeAsync();

            result.IsSystemUser.Should().BeTrue();
            result.AssignableRoles.Should().Equal("guest", "manager");
        }
    }
}
