using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    public class ResourceQueryServiceTests : IDisposable
    {
        private readonly Mock<IResourceRepository> _repo = new();

        private ResourceQueryService Create() =>
            new(NullLogger<ResourceQueryService>.Instance, _repo.Object);

        private static void SetContext(
            string orgId = "default",
            IEnumerable<string>? roles = null,
            IEnumerable<string>? permissions = null)
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: roles, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
                permissions: permissions, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        // ---------- GetPermissionAsync ----------

        [Fact]
        public async Task GetPermission_ReturnsPermissionFromRepository()
        {
            var permission = new Permission { ItemId = "p1", Name = "Read", Resource = "res.read" };
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(permission);

            var result = await Create().GetPermissionAsync("p1");

            result.Data.Should().BeSameAs(permission);
            _repo.Verify(r => r.GetPermissionByIdAsync("p1"), Times.Once);
        }

        [Fact]
        public async Task GetPermission_ReturnsNullData_WhenRepositoryReturnsNull()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("missing")).ReturnsAsync((Permission)null!);

            var result = await Create().GetPermissionAsync("missing");

            result.Data.Should().BeNull();
        }

        // ---------- GetPermissionsGroupBySeverityAsync ----------

        [Fact]
        public async Task GetPermissionsGroupBySeverity_DelegatesToRepository()
        {
            var groups = new List<PermissionGroupBySeverityResponse>
            {
                new() { SeverityLevel = "High", Count = 3 },
                new() { SeverityLevel = "Low", Count = 7 },
            };
            _repo.Setup(r => r.GetPermissionsGroupBySeverityAsync(It.IsAny<string>())).ReturnsAsync(groups);

            var result = await Create().GetPermissionsGroupBySeverityAsync();

            result.Should().BeSameAs(groups);
            result.Should().HaveCount(2);
        }

        // ---------- GetPermissionsAsync (orgId branching) ----------

        [Fact]
        public async Task GetPermissions_DefaultOrgWithQueryOrg_PassesQueryOrganizationId()
        {
            SetContext(orgId: "default");
            var perms = new List<Permission> { new() { ItemId = "p1" } }.AsQueryable();
            _repo.Setup(r => r.GetPermissionsAsync(It.IsAny<GetPermissionsRequest>(), It.IsAny<string>()))
                .ReturnsAsync((perms, 5L));

            var query = new GetPermissionsRequest { OrganizationId = "org-x" };
            var result = await Create().GetPermissionsAsync(query);

            result.Data.Should().BeSameAs(perms);
            result.TotalCount.Should().Be(5);
            _repo.Verify(r => r.GetPermissionsAsync(query, "org-x"), Times.Once);
        }

        [Fact]
        public async Task GetPermissions_DefaultOrgWithoutQueryOrg_PassesNull()
        {
            SetContext(orgId: "default");
            var perms = new List<Permission>().AsQueryable();
            _repo.Setup(r => r.GetPermissionsAsync(It.IsAny<GetPermissionsRequest>(), It.IsAny<string>()))
                .ReturnsAsync((perms, 0L));

            var query = new GetPermissionsRequest { OrganizationId = "   " };
            await Create().GetPermissionsAsync(query);

            _repo.Verify(r => r.GetPermissionsAsync(query, It.Is<string>(o => o == null)), Times.Once);
        }

        [Fact]
        public async Task GetPermissions_NonDefaultOrg_PassesNull_EvenWhenQueryOrgSet()
        {
            SetContext(orgId: "org-42");
            var perms = new List<Permission>().AsQueryable();
            _repo.Setup(r => r.GetPermissionsAsync(It.IsAny<GetPermissionsRequest>(), It.IsAny<string>()))
                .ReturnsAsync((perms, 0L));

            var query = new GetPermissionsRequest { OrganizationId = "org-x" };
            await Create().GetPermissionsAsync(query);

            _repo.Verify(r => r.GetPermissionsAsync(query, It.Is<string>(o => o == null)), Times.Once);
        }

        // ---------- GetRoleAsync ----------

        [Fact]
        public async Task GetRole_ReturnsRoleFromRepository()
        {
            var role = new Role { ItemId = "r1", Slug = "admin", Name = "Admin" };
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(role);

            var result = await Create().GetRoleAsync("r1");

            result.Data.Should().BeSameAs(role);
        }

        // ---------- GetRolesAsync (orgId branching) ----------

        [Fact]
        public async Task GetRoles_DefaultOrgWithQueryOrg_PassesQueryOrganizationId()
        {
            SetContext(orgId: "default");
            var roles = new List<Role> { new() { Slug = "a" } }.AsQueryable();
            _repo.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((roles, 1L));

            var query = new GetRolesRequest { OrganizationId = "org-x" };
            var result = await Create().GetRolesAsync(query);

            result.Data.Should().BeSameAs(roles);
            result.TotalCount.Should().Be(1);
            _repo.Verify(r => r.GetRolesAsync(query, "org-x"), Times.Once);
        }

        [Fact]
        public async Task GetRoles_NonDefaultOrg_PassesNull()
        {
            SetContext(orgId: "org-9");
            var roles = new List<Role>().AsQueryable();
            _repo.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((roles, 0L));

            var query = new GetRolesRequest { OrganizationId = "org-x" };
            await Create().GetRolesAsync(query);

            _repo.Verify(r => r.GetRolesAsync(query, It.Is<string>(o => o == null)), Times.Once);
        }

        // ---------- GetResourceGroupsAsync ----------

        [Fact]
        public async Task GetResourceGroups_DelegatesToRepository()
        {
            var groups = new List<GetResourceGroupResponse>
            {
                new() { ResourceGroup = "Billing", Count = 4 },
            };
            _repo.Setup(r => r.GetResourceGroupsAsync(It.IsAny<string>())).ReturnsAsync(groups);

            var result = await Create().GetResourceGroupsAsync();

            result.Should().BeSameAs(groups);
        }

        // ---------- GetFeResourceFeaturesAsync ----------

        [Fact]
        public async Task GetFeResourceFeatures_ReturnsEmpty_WhenNoRolesAndNoPermissions()
        {
            SetContext(roles: new List<string>(), permissions: new List<string>());

            var result = await Create().GetFeResourceFeaturesAsync(new GetFeResourceFeatureRequest());

            result.Should().BeEmpty();
            _repo.Verify(r => r.GetFeResourceFeaturesAsync(
                It.IsAny<List<string>>(), It.IsAny<List<string>>(),
                It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetFeResourceFeatures_FiltersFrontendActionsAndMaps()
        {
            SetContext(roles: new List<string> { "admin" }, permissions: new List<string> { "read" });

            var repoData = new List<Permission>
            {
                new() { Type = ResourceType.FrontendAction, Resource = "ui.view", Name = "View", Description = "d1" },
                new() { Type = ResourceType.Endpoint, Resource = "api.get", Name = "Get", Description = "d2" },
                new() { Type = ResourceType.FrontendAction, Resource = "ui.edit", Name = "Edit", Description = "d3" },
            };
            _repo.Setup(r => r.GetFeResourceFeaturesAsync(
                    It.IsAny<List<string>>(), It.IsAny<List<string>>(),
                    "srch", true, It.IsAny<string>()))
                .ReturnsAsync(repoData);

            var request = new GetFeResourceFeatureRequest { Search = "srch", IsBuiltIn = true };
            var result = await Create().GetFeResourceFeaturesAsync(request);

            result.Should().HaveCount(2);
            result.Select(x => x.Resource).Should().BeEquivalentTo("ui.view", "ui.edit");
            result.First(x => x.Resource == "ui.view").Name.Should().Be("View");
            result.First(x => x.Resource == "ui.view").Description.Should().Be("d1");

            _repo.Verify(r => r.GetFeResourceFeaturesAsync(
                It.Is<List<string>>(l => l.Contains("admin")),
                It.Is<List<string>>(l => l.Contains("read")),
                "srch", true, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetFeResourceFeatures_ReturnsEmpty_WhenNoFrontendActions()
        {
            SetContext(roles: new List<string> { "admin" });

            var repoData = new List<Permission>
            {
                new() { Type = ResourceType.Endpoint, Resource = "api.get", Name = "Get" },
            };
            _repo.Setup(r => r.GetFeResourceFeaturesAsync(
                    It.IsAny<List<string>>(), It.IsAny<List<string>>(),
                    It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<string>()))
                .ReturnsAsync(repoData);

            var result = await Create().GetFeResourceFeaturesAsync(new GetFeResourceFeatureRequest());

            result.Should().BeEmpty();
        }

        // ---------- GetAssignableRolesAsync ----------

        [Fact]
        public async Task GetAssignableRoles_ClassifiesHierarchyAndStandalone()
        {
            SetContext(roles: new List<string> { "manager" });

            var roles = new List<Role>
            {
                new() { Slug = "manager", Name = "Manager", CanCreateOwn = true, ParentRoleSlug = null, AncestorRoleSlugs = new() },
                new() { Slug = "team-lead", Name = "Team Lead", CanCreateOwn = true, ParentRoleSlug = "manager", AncestorRoleSlugs = new() { "manager" } },
                new() { Slug = "admin", Name = "Admin", CanCreateOwn = true, ParentRoleSlug = null, AncestorRoleSlugs = new() },
                new() { Slug = "guest", Name = "Guest", CanCreateOwn = false, ParentRoleSlug = null, AncestorRoleSlugs = new() },
            }.AsQueryable();
            _repo.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((roles, 4L));

            var result = await Create().GetAssignableRolesAsync();

            result.Hierarchy.Select(x => x.Slug).Should().BeEquivalentTo("manager", "team-lead");
            result.Standalone.Select(x => x.Slug).Should().BeEquivalentTo("guest");
            // admin is neither a descendant of a creatable role nor standalone -> excluded
            result.Hierarchy.Select(x => x.Slug).Should().NotContain("admin");
            result.Standalone.Select(x => x.Slug).Should().NotContain("admin");
        }

        [Fact]
        public async Task GetAssignableRoles_RequestsLargePage()
        {
            SetContext(roles: new List<string> { "manager" });
            var roles = new List<Role>().AsQueryable();
            _repo.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((roles, 0L));

            await Create().GetAssignableRolesAsync();

            _repo.Verify(r => r.GetRolesAsync(It.Is<GetRolesRequest>(q => q.PageSize == 1000), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetAssignableRoles_NoUserRoles_YieldsOnlyStandalone()
        {
            SetContext(roles: new List<string>());

            var roles = new List<Role>
            {
                new() { Slug = "manager", Name = "Manager", CanCreateOwn = true, AncestorRoleSlugs = new() },
                new() { Slug = "guest", Name = "Guest", CanCreateOwn = false, AncestorRoleSlugs = new() },
            }.AsQueryable();
            _repo.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((roles, 2L));

            var result = await Create().GetAssignableRolesAsync();

            result.Hierarchy.Should().BeEmpty();
            result.Standalone.Select(x => x.Slug).Should().BeEquivalentTo("guest");
        }

        [Fact]
        public async Task GetAssignableRoles_MatchesUserRolesCaseInsensitively()
        {
            SetContext(roles: new List<string> { "MANAGER" });

            var roles = new List<Role>
            {
                new() { Slug = "manager", Name = "Manager", CanCreateOwn = true, AncestorRoleSlugs = new() },
            }.AsQueryable();
            _repo.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((roles, 1L));

            var result = await Create().GetAssignableRolesAsync();

            result.Hierarchy.Select(x => x.Slug).Should().BeEquivalentTo("manager");
        }

        [Fact]
        public async Task GetAssignableRoles_RoleReferencedAsAncestor_IsNotStandalone()
        {
            // "root" cannot create own, has no parent/ancestors, but is referenced as an
            // ancestor by "child" -> excluded from standalone. Since no user role can create
            // it, it lands in neither bucket.
            SetContext(roles: new List<string>());

            var roles = new List<Role>
            {
                new() { Slug = "root", Name = "Root", CanCreateOwn = false, ParentRoleSlug = null, AncestorRoleSlugs = new() },
                new() { Slug = "child", Name = "Child", CanCreateOwn = true, ParentRoleSlug = "root", AncestorRoleSlugs = new() { "root" } },
            }.AsQueryable();
            _repo.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((roles, 2L));

            var result = await Create().GetAssignableRolesAsync();

            result.Standalone.Should().BeEmpty();
            result.Hierarchy.Should().BeEmpty();
        }

        // ---------- Archive impact preview (#464) ----------

        private static Role ImpactRole(string org = "default") => new()
        {
            ItemId = "r1", Slug = "manager", Name = "Manager", Description = "d", OrganizationId = org
        };

        private static Role ImpactCopy(string itemId, string org) => new()
        {
            ItemId = itemId, Slug = "manager", Name = "Manager", Description = "d",
            OrganizationId = org, CreatedFromDefault = true
        };

        private void MultiOrg(bool enabled) =>
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(enabled ? new TenantConfiguration { IsMultiOrgEnabled = true } : null!);

        [Fact]
        public async Task RoleArchiveImpact_NotFound_ReturnsErrorAndCountsNothing()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("nope")).ReturnsAsync((Role)null!);

            var result = await Create().GetRoleArchiveImpactAsync("nope");

            result.IsSuccess.Should().BeFalse();
            result.Errors!["ItemId"].Should().Be("Role_Not_Found");
            _repo.Verify(r => r.CountUsersWithRoleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task RoleArchiveImpact_CountsOtherOrganizationsAndUsers()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(ImpactRole());
            MultiOrg(true);
            _repo.Setup(r => r.GetNonArchivedRolesBySlugAsync("manager"))
                .ReturnsAsync(new List<Role> { ImpactRole(), ImpactCopy("c-acme", "acme"), ImpactCopy("c-globex", "globex") });
            _repo.Setup(r => r.CountUsersWithRoleAsync("manager", It.IsAny<IEnumerable<string>>(), false)).ReturnsAsync(3);
            _repo.Setup(r => r.CountUsersWithRoleAsync("manager", It.IsAny<IEnumerable<string>>(), true)).ReturnsAsync(3);

            var result = await Create().GetRoleArchiveImpactAsync("r1");

            result.IsSuccess.Should().BeTrue();
            result.Slug.Should().Be("manager");
            result.IsMultiOrgEnabled.Should().BeTrue();
            // "2 OTHER organizations" -- the target's own organization is never one of them.
            result.OrganizationCount.Should().Be(2);
            result.AffectedUserCount.Should().Be(3);
            result.ActiveUserCount.Should().Be(3);
            result.Blocked.Should().BeFalse();
        }

        [Fact]
        public async Task RoleArchiveImpact_ReportsWhetherTheRoleIsASignUpDefault()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(ImpactRole());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration
            {
                DefaultRolesForNewUserOnSignUp = new List<string> { "viewer", "MANAGER" }
            });

            var result = await Create().GetRoleArchiveImpactAsync("r1");

            // Case-insensitive: the signup list is stored as the client sent it and is not
            // normalised on save, unlike the slug on the role itself.
            result.IsSignUpDefault.Should().BeTrue();
        }

        [Fact]
        public async Task RoleArchiveImpact_NotASignUpDefault_ReportsFalse()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(ImpactRole());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration
            {
                DefaultRolesForNewUserOnSignUp = new List<string> { "viewer" },
                // The permission list must never answer the role question.
                DefaultPermissionsForNewUserOnSignUp = new List<string> { "manager" }
            });

            var result = await Create().GetRoleArchiveImpactAsync("r1");

            result.IsSignUpDefault.Should().BeFalse();
        }

        [Fact]
        public async Task RoleArchiveImpact_NoTenantConfiguration_ReportsNotASignUpDefault()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(ImpactRole());
            MultiOrg(false);

            var result = await Create().GetRoleArchiveImpactAsync("r1");

            // A null configuration document must read as "not a default" rather than throwing.
            result.IsSignUpDefault.Should().BeFalse();
        }

        [Fact]
        public async Task RoleArchiveImpact_ArchivedCopyWidensNeitherCount()
        {
            IEnumerable<string>? scoped = null;
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(ImpactRole());
            MultiOrg(true);
            // GetNonArchivedRolesBySlugAsync filters archived copies at the source, so globex is absent.
            _repo.Setup(r => r.GetNonArchivedRolesBySlugAsync("manager"))
                .ReturnsAsync(new List<Role> { ImpactRole(), ImpactCopy("c-acme", "acme") });
            _repo.Setup(r => r.CountUsersWithRoleAsync("manager", It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                .Callback<string, IEnumerable<string>, bool>((_, orgs, _) => scoped = orgs)
                .ReturnsAsync(1);

            var result = await Create().GetRoleArchiveImpactAsync("r1");

            result.OrganizationCount.Should().Be(1);
            // An archived organization must not widen the user scope either, or the preview would
            // promise to revoke from users the archive will never reach.
            scoped!.Should().BeEquivalentTo(new[] { "acme", "default" });
        }

        [Fact]
        public async Task RoleArchiveImpact_SingleOrgTenant_ReportsNoOrganizationsAndDoesNotThrow()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(ImpactRole());
            MultiOrg(false);
            _repo.Setup(r => r.CountUsersWithRoleAsync("manager", It.IsAny<IEnumerable<string>>(), false)).ReturnsAsync(1);
            _repo.Setup(r => r.CountUsersWithRoleAsync("manager", It.IsAny<IEnumerable<string>>(), true)).ReturnsAsync(0);

            var result = await Create().GetRoleArchiveImpactAsync("r1");

            // A null TenantConfiguration must read as "off" rather than throwing.
            result.IsMultiOrgEnabled.Should().BeFalse();
            result.OrganizationCount.Should().Be(0);
            result.AffectedUserCount.Should().Be(1);
            result.ActiveUserCount.Should().Be(0);
            _repo.Verify(r => r.GetNonArchivedRolesBySlugAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RoleArchiveImpact_ChildRoles_BlocksButStillReportsCounts()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(ImpactRole());
            MultiOrg(true);
            _repo.Setup(r => r.GetNonArchivedRolesBySlugAsync("manager"))
                .ReturnsAsync(new List<Role> { ImpactRole(), ImpactCopy("c-acme", "acme") });
            _repo.Setup(r => r.HasChildRolesAsync("manager", "default")).ReturnsAsync(true);
            _repo.Setup(r => r.CountUsersWithRoleAsync("manager", It.IsAny<IEnumerable<string>>(), It.IsAny<bool>())).ReturnsAsync(4);

            var result = await Create().GetRoleArchiveImpactAsync("r1");

            result.Blocked.Should().BeTrue();
            result.BlockingReason.Should().Be("Role_Has_Child_Roles");
            // Blocked is not an error: the dialog still needs the numbers to explain the refusal.
            result.IsSuccess.Should().BeTrue();
            result.OrganizationCount.Should().Be(1);
            result.AffectedUserCount.Should().Be(4);
        }

        [Fact]
        public async Task RoleArchiveImpact_NothingAffected_ReturnsZeros()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(ImpactRole());
            MultiOrg(true);
            _repo.Setup(r => r.GetNonArchivedRolesBySlugAsync("manager")).ReturnsAsync(new List<Role> { ImpactRole() });

            var result = await Create().GetRoleArchiveImpactAsync("r1");

            result.OrganizationCount.Should().Be(0);
            result.AffectedUserCount.Should().Be(0);
            result.Blocked.Should().BeFalse();
        }

        [Fact]
        public async Task PermissionArchiveImpact_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("nope")).ReturnsAsync((Permission)null!);

            var result = await Create().GetPermissionArchiveImpactAsync("nope");

            result.IsSuccess.Should().BeFalse();
            result.Errors!["ItemId"].Should().Be("Permission_Not_Found");
        }

        [Fact]
        public async Task PermissionArchiveImpact_CountsDirectGrantsAndRoleBindingsSeparately()
        {
            var permission = new Permission { ItemId = "p1", Name = "Export", Resource = "reports::export", OrganizationId = "default" };
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(permission);
            MultiOrg(true);
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                permission,
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme" },
                new() { ItemId = "p-globex", Resource = "reports::export", OrganizationId = "globex" }
            });
            _repo.Setup(r => r.CountUsersWithPermissionAsync("reports::export", It.IsAny<IEnumerable<string>>())).ReturnsAsync(2);
            _repo.Setup(r => r.CountRoleBindingsForResourceAsync("reports::export", It.IsAny<IEnumerable<string>>())).ReturnsAsync(3);

            var result = await Create().GetPermissionArchiveImpactAsync("p1");

            result.IsSuccess.Should().BeTrue();
            result.Resource.Should().Be("reports::export");
            result.OrganizationCount.Should().Be(2);
            // Direct User.Permissions grants and Permission.Roles bindings are different
            // populations. Only the first mints a token claim, so conflating them would misreport
            // who actually loses access.
            result.AffectedUserCount.Should().Be(2);
            result.RoleBindingCount.Should().Be(3);
            result.Blocked.Should().BeFalse();
            result.BlockingReason.Should().BeNull();
        }

        [Fact]
        public async Task PermissionArchiveImpact_ReportsWhetherThePermissionIsASignUpDefault()
        {
            var permission = new Permission { ItemId = "p1", Name = "Export", Resource = "reports::export", OrganizationId = "default" };
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(permission);
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration
            {
                DefaultPermissionsForNewUserOnSignUp = new List<string> { "reports::export" },
                // The role list must never answer the permission question.
                DefaultRolesForNewUserOnSignUp = new List<string> { "manager" }
            });

            var result = await Create().GetPermissionArchiveImpactAsync("p1");

            result.IsSignUpDefault.Should().BeTrue();
        }

        [Fact]
        public async Task PermissionArchiveImpact_NotASignUpDefault_ReportsFalse()
        {
            var permission = new Permission { ItemId = "p1", Name = "Export", Resource = "reports::export", OrganizationId = "default" };
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(permission);
            MultiOrg(false);

            var result = await Create().GetPermissionArchiveImpactAsync("p1");

            result.IsSignUpDefault.Should().BeFalse();
        }

        [Fact]
        public async Task PermissionArchiveImpact_ExcludesArchivedCopies()
        {
            var permission = new Permission { ItemId = "p1", Resource = "reports::export", OrganizationId = "default" };
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(permission);
            MultiOrg(true);
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                permission,
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme" },
                new() { ItemId = "p-globex", Resource = "reports::export", OrganizationId = "globex", IsArchived = true }
            });

            var result = await Create().GetPermissionArchiveImpactAsync("p1");

            result.OrganizationCount.Should().Be(1);
        }

        // ---------- GetRolePermissionChangeImpactAsync ----------

        private static RolePermissionChangeImpactRequest ChangeRequest(
            string org = "default", List<string>? add = null, List<string>? remove = null) => new()
            {
                Slug = "manager",
                OrganizationId = org,
                AddPermissions = add ?? new List<string> { "p-default" },
                RemovePermissions = remove ?? new List<string>()
            };

        private void GivenRoleInEveryOrg(bool multiOrg = true)
        {
            SetContext();
            MultiOrg(multiOrg);
            _repo.Setup(r => r.GetRoleBySlugAsync("manager", It.IsAny<string>()))
                .ReturnsAsync((string slug, string org) => new Role
                {
                    ItemId = "r-" + org, Slug = slug, Name = "Manager", Description = "d", OrganizationId = org
                });
            _repo.Setup(r => r.GetNonArchivedRolesBySlugAsync("manager"))
                .ReturnsAsync(new List<Role>
                {
                    ImpactCopy("r-default", "default"), ImpactCopy("c-acme", "acme"), ImpactCopy("c-globex", "globex")
                });
            _repo.Setup(r => r.GetAllOrgIdsAsync())
                .ReturnsAsync(new List<string> { "default", "acme", "globex" });
            _repo.Setup(r => r.GetPermissionsByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new List<Permission> { new() { ItemId = "p-default", Resource = "reports::export" } });
            _repo.Setup(r => r.CountUsersWithRoleAsync("manager", It.IsAny<IEnumerable<string>>(), false)).ReturnsAsync(7);
            _repo.Setup(r => r.CountUsersWithRoleAsync("manager", It.IsAny<IEnumerable<string>>(), true)).ReturnsAsync(4);
        }

        [Fact]
        public async Task PermissionChangeImpact_EmptySlug_IsRejectedBeforeAnyLookup()
        {
            var result = await Create().GetRolePermissionChangeImpactAsync(
                new RolePermissionChangeImpactRequest { Slug = "  " });

            result.IsSuccess.Should().BeFalse();
            result.Errors!["Slug"].Should().Be("Slug_Never_Empty");
            _repo.Verify(r => r.GetRoleBySlugAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task PermissionChangeImpact_RoleNotFound_ReturnsErrorAndCountsNothing()
        {
            SetContext();
            _repo.Setup(r => r.GetRoleBySlugAsync("manager", It.IsAny<string>())).ReturnsAsync((Role)null!);

            var result = await Create().GetRolePermissionChangeImpactAsync(ChangeRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors!["Role"].Should().Be("Role_Not_Found");
            _repo.Verify(r => r.CountUsersWithRoleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// SetRolesAsync refuses an archived role. A preview that happily described the change
        /// would be offering a confirmation the save is guaranteed to reject.
        /// </summary>
        [Fact]
        public async Task PermissionChangeImpact_ArchivedRole_IsRefusedLikeTheSaveWouldBe()
        {
            SetContext();
            _repo.Setup(r => r.GetRoleBySlugAsync("manager", It.IsAny<string>()))
                .ReturnsAsync(new Role
                {
                    ItemId = "r1", Slug = "manager", Name = "Manager", Description = "d",
                    OrganizationId = "default", IsArchived = true
                });

            var result = await Create().GetRolePermissionChangeImpactAsync(ChangeRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors!["archived"].Should().Be("Role_Already_Archived");
        }

        [Fact]
        public async Task PermissionChangeImpact_DefaultOrgMultiOrg_OffersPropagationWithCounts()
        {
            GivenRoleInEveryOrg();

            var result = await Create().GetRolePermissionChangeImpactAsync(ChangeRequest());

            result.IsSuccess.Should().BeTrue();
            result.CanPropagate.Should().BeTrue();
            // "2 OTHER organizations" -- the role's own is never one of them.
            result.OrganizationCount.Should().Be(2);
            result.AddCount.Should().Be(1);
            result.RemoveCount.Should().Be(0);
            result.AffectedUserCount.Should().Be(7);
            result.ActiveUserCount.Should().Be(4);
            result.SkippedOrganizationCount.Should().Be(0);
        }

        /// <summary>
        /// The half of the gate that is easy to forget. ProcessPermissionAsync ignores the flag for
        /// an organization-scoped caller, so the preview must not offer the option to one.
        /// </summary>
        [Fact]
        public async Task PermissionChangeImpact_OrganizationScopedRole_DoesNotOfferPropagation()
        {
            GivenRoleInEveryOrg();

            var result = await Create().GetRolePermissionChangeImpactAsync(ChangeRequest(org: "acme"));

            result.IsSuccess.Should().BeTrue();
            result.IsMultiOrgEnabled.Should().BeTrue();
            result.CanPropagate.Should().BeFalse();
            result.OrganizationCount.Should().Be(0);
        }

        [Fact]
        public async Task PermissionChangeImpact_SingleOrgTenant_DoesNotOfferPropagation()
        {
            GivenRoleInEveryOrg(multiOrg: false);

            var result = await Create().GetRolePermissionChangeImpactAsync(ChangeRequest());

            result.IsSuccess.Should().BeTrue();
            result.IsMultiOrgEnabled.Should().BeFalse();
            result.CanPropagate.Should().BeFalse();
            result.OrganizationCount.Should().Be(0);
        }

        /// <summary>
        /// An organization with no live copy of the role is silently stepped over by the
        /// propagation. Counting it here is what turns that silence into something the
        /// administrator can see before agreeing.
        /// </summary>
        [Fact]
        public async Task PermissionChangeImpact_OrganizationWithoutTheRole_IsReportedAsSkipped()
        {
            GivenRoleInEveryOrg();
            _repo.Setup(r => r.GetNonArchivedRolesBySlugAsync("manager"))
                .ReturnsAsync(new List<Role> { ImpactCopy("r-default", "default"), ImpactCopy("c-acme", "acme") });

            var result = await Create().GetRolePermissionChangeImpactAsync(ChangeRequest());

            result.OrganizationCount.Should().Be(1);
            result.SkippedOrganizationCount.Should().Be(1);
        }

        /// <summary>
        /// Counts come from resolved documents, not from the ids the caller sent: an id with no
        /// document behind it binds nothing, and promising it would be a lie the save cannot keep.
        /// </summary>
        [Fact]
        public async Task PermissionChangeImpact_UnresolvableIds_AreNotCounted()
        {
            GivenRoleInEveryOrg();
            _repo.Setup(r => r.GetPermissionsByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new List<Permission>());

            var result = await Create().GetRolePermissionChangeImpactAsync(
                ChangeRequest(add: new List<string> { "ghost-1", "ghost-2" }));

            result.AddCount.Should().Be(0);
        }

        [Fact]
        public async Task PermissionChangeImpact_Removals_CountTheUsersWhoWouldLoseThem()
        {
            GivenRoleInEveryOrg();

            var result = await Create().GetRolePermissionChangeImpactAsync(ChangeRequest(
                add: new List<string>(),
                remove: new List<string> { "p-default" }));

            result.RemoveCount.Should().Be(1);
            result.AddCount.Should().Be(0);
            result.AffectedUserCount.Should().Be(7);
            result.ActiveUserCount.Should().Be(4);
        }
    }
}
