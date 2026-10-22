using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.DelegatedAdministration;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.Resources
{
    /// <summary>
    /// Delegated administration on the role, permission, organization and configuration paths:
    /// permissions (R6), roles/create, roles/update, DELETE roles/{id}, roles/assign-permissions (R5),
    /// organizations/create (R7), organizations/config (R6), and the always-on roles/update
    /// organization pin. The caller "caller" holds "admin" in org-acme over the standard tree.
    /// </summary>
    public sealed class ResourceMutationDelegationTests : IDisposable
    {
        private const string Org = DelegationTestKit.Org;

        private readonly DelegationTestKit _kit = new DelegationTestKit().WithStandardTree();
        private readonly Mock<IResourceRepository> _repo = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IValidator<CreateRoleRequest>> _roleValidator = new();
        private readonly Mock<IUserRepository> _users = new();
        private readonly List<Role> _inserted = [];
        private readonly List<Role> _bulkUpdated = [];

        public ResourceMutationDelegationTests()
        {
            _kit.WithUser("caller", ["admin"]);
            DelegationTestKit.SignIn("caller");

            _roleValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new IamTenantConfiguration
            {
                IsMultiOrgEnabled = true,
                AllowOrgCreationFromPortal = true,
                AllowOrgCreationFromCloud = true
            });
            _repo.Setup(r => r.GetOrganizationById(It.IsAny<string>())).ReturnsAsync((string id) => new Organization { ItemId = id, Name = id });
            _repo.Setup(r => r.GetRoleByIdAsync(It.IsAny<string>()))
                .ReturnsAsync((string id) => AllRoles().FirstOrDefault(r => r.ItemId == id)!);
            _repo.Setup(r => r.GetRoleBySlugAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string slug, string? org) => _kit.RolesOf(string.IsNullOrWhiteSpace(org) ? Org : org).FirstOrDefault(r => r.Slug == slug)!);
            _repo.Setup(r => r.GetRolesByOrgAsync(It.IsAny<string>())).ReturnsAsync((string org) => _kit.RolesOf(org).ToList());
            _repo.Setup(r => r.GetRolesBySlugAsync(It.IsAny<string>())).ReturnsAsync([]);
            _repo.Setup(r => r.InsertRoleAsync(It.IsAny<Role>())).Callback<Role>(_inserted.Add).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>())).Callback<List<Role>>(_bulkUpdated.AddRange).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRoleFromAllPermissionsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRoleFromAllUsersAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRoleFromSignUpDefaultsAsync(It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);
            _iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
        }

        public void Dispose() => DelegationTestKit.SignOut();

        private IEnumerable<Role> AllRoles() => _kit.RolesOf(Org).Concat(_kit.RolesOf("org-other")).Concat(_kit.RolesOf("default"));

        private Role RoleNamed(string slug, string org = Org) => _kit.RolesOf(org).Single(r => r.Slug == slug);

        private ResourceMutationService Create(bool wired = true) =>
            new(NullLogger<ResourceMutationService>.Instance, _repo.Object, _iam.Object,
                Mock.Of<IValidator<CreatePermissionRequest>>(), Mock.Of<IValidator<UpdatePermissionRequest>>(), _roleValidator.Object,
                Mock.Of<ITenantPermissionPropagator>(), Mock.Of<IUserActivityDispatcher>(), _users.Object,
                delegatedAdministration: wired ? _kit.Service() : null);

        private static void ShouldBeRefused(BaseResponse result) =>
            result.Errors.Should().ContainKey(DelegatedAdministrationErrors.GrantNotAllowed);

        private void Audit() =>
            _kit.Options.CurrentValue = new DelegatedAdministrationOptions { Mode = DelegatedAdministrationMode.Audit };

        // ---------- R6 permissions ----------

        [Fact]
        public async Task PermissionWritesBelongToTheSystemUser()
        {
            var service = Create();

            ShouldBeRefused(await service.CreatePermissionAsync(new CreatePermissionRequest { Name = "x", Resource = "x" }));
            ShouldBeRefused(await service.UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "x" }));
            ShouldBeRefused(await service.ArchivePermissionAsync("p1"));
        }

        [Fact]
        public async Task AuditKeepsTheExistingDefaultOrganizationRefusalForPermissions()
        {
            Audit();

            var result = await Create().CreatePermissionAsync(new CreatePermissionRequest { Name = "x", Resource = "x" });

            result.Errors.Should().ContainKey("forbidden");
            _kit.Logged($"DelegatedAdministration would deny R6-SystemOnly caller {Org} permissions/create").Should().BeTrue();
        }

        // ---------- R5 roles/create ----------

        [Theory]
        [InlineData(null, true)]
        [InlineData("admin", true)]
        [InlineData("viewer", true)]
        [InlineData("owner", false)]
        public async Task CreateRoleHangsOnlyBelowAHeldOrManagedRole(string? parent, bool allowed)
        {
            var result = await Create().CreateRoleAsync(new CreateRoleRequest { Name = "Team", Slug = "team", ParentRoleSlug = parent });

            if (allowed)
            {
                result.IsSuccess.Should().BeTrue();
                _inserted.Should().ContainSingle();
            }
            else
            {
                ShouldBeRefused(result);
                _inserted.Should().BeEmpty();
            }
        }

        // ---------- roles/update ----------

        [Fact]
        public async Task UpdateRoleOfAnotherOrganizationIsAlwaysRefused()
        {
            _kit.WithRole("theirs", organizationId: "org-other");

            var result = await Create(wired: false).UpdateRoleAsync(new UpdateRoleRequest { ItemId = RoleNamed("theirs", "org-other").ItemId, Name = "Mine now" });

            result.Errors.Should().ContainKey("forbidden");
            result.Errors!["forbidden"].Should().Be("Not_Allowed_To_Update_Role_From_Another_Organization");
            _repo.Verify(r => r.UpdateRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task UpdateRoleOfAnotherOrganizationStaysOpenToTheTenantWideCaller()
        {
            DelegationTestKit.SignIn("caller", "default");
            _kit.WithRole("theirs", organizationId: "org-other");

            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = RoleNamed("theirs", "org-other").ItemId, Name = "Renamed" });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateOwnRoleOrARoleAboveTheCallerIsRefused()
        {
            ShouldBeRefused(await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "role-admin", Name = "Admin" }));
            ShouldBeRefused(await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "role-owner", Name = "Owner" }));
            (await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "role-manager", Name = "Lead", ParentRoleSlug = "admin" }))
                .IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task MovingARoleBelowARoleTheCallerDoesNotHoldIsRefused()
        {
            ShouldBeRefused(await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "role-viewer", Name = "Viewer", ParentRoleSlug = "auditor" }));
        }

        [Fact]
        public async Task MovingARoleBelowItsOwnDescendantIsACycle()
        {
            _kit.WithStandardTree("default");
            DelegationTestKit.SignIn("caller", "default");
            _repo.Setup(r => r.GetRoleBySlugAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string slug, string? _) => _kit.RolesOf("default").FirstOrDefault(r => r.Slug == slug)!);
            var admin = RoleNamed("admin", "default");

            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = admin.ItemId, Name = "Admin", ParentRoleSlug = "viewer" });

            result.Errors!["ParentRoleSlug"].Should().Be("Role_Hierarchy_Cycle_Detected");
            _repo.Verify(r => r.UpdateRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task MovingARoleRebuildsTheAncestorsOfEveryRoleBelowIt()
        {
            _kit.WithRole("lead", organizationId: Org); // a new top-level role the caller will hold
            _kit.WithUser("caller", ["admin", "lead"]);

            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "role-manager", Name = "Manager", ParentRoleSlug = "lead" });

            result.IsSuccess.Should().BeTrue();
            RoleNamed("manager").AncestorRoleSlugs.Should().Equal("lead");
            _bulkUpdated.Should().ContainSingle(r => r.Slug == "viewer")
                .Which.AncestorRoleSlugs.Should().Equal("manager", "lead");
        }

        [Fact]
        public async Task MovingARoleToTheTopClearsItsAncestors()
        {
            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "role-manager", Name = "Manager", ParentRoleSlug = null });

            result.IsSuccess.Should().BeTrue();
            RoleNamed("manager").AncestorRoleSlugs.Should().BeEmpty();
            _bulkUpdated.Should().ContainSingle(r => r.Slug == "viewer").Which.AncestorRoleSlugs.Should().Equal("manager");
        }

        // ---------- DELETE roles/{id} ----------

        [Fact]
        public async Task ArchivingOwnRoleOrARoleAboveTheCallerIsRefused()
        {
            ShouldBeRefused(await Create().ArchiveRoleAsync("role-admin"));
            ShouldBeRefused(await Create().ArchiveRoleAsync("role-owner"));
            _repo.Verify(r => r.RemoveRoleFromAllUsersAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ArchivingARoleEndsTheSessionsOfItsHolders()
        {
            _kit.WithUser("member", ["auditor"]);

            var result = await Create().ArchiveRoleAsync("role-auditor", confirmRevokeFromUsers: true);

            result.IsSuccess.Should().BeTrue();
            _kit.Terminator.Verify(t => t.EndAllSessionsAsync("member", "role_archived"), Times.Once);
        }

        // ---------- roles/assign-permissions ----------

        [Fact]
        public async Task AssigningAPermissionTheCallerDoesNotHoldIsRefused()
        {
            var service = Create();

            var refused = await service.SetRolesAsync(new SetRolesRequest { Slug = "viewer", AddPermissions = ["perm-billing"] });
            var allowed = await service.SetRolesAsync(new SetRolesRequest { Slug = "viewer", AddPermissions = ["perm-write"], RemovePermissions = ["perm-read"] });

            refused.Errors.Should().ContainKey(DelegatedAdministrationErrors.GrantNotAllowed);
            allowed.Success.Should().BeTrue();
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("viewer", It.Is<List<string>>(l => l.Contains("perm-billing")), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ChangingPermissionsOfOwnRoleIsRefused()
        {
            var result = await Create().SetRolesAsync(new SetRolesRequest { Slug = "admin", RemovePermissions = ["perm-read"] });

            result.Errors.Should().ContainKey(DelegatedAdministrationErrors.GrantNotAllowed);
        }

        // ---------- organizations/create (R7) ----------

        [Fact]
        public async Task CreatorMayOnlyBeGivenRolesTheCallerCouldAssign()
        {
            var refused = await Create().CreateOrganizationAsync(new CreateOrganizationRequest
            {
                Name = "New",
                CreatedFrom = CreatedFrom.ConstructPortal,
                DefaultRoleForMembers = ["owner"]
            });

            ShouldBeRefused(refused);
            _repo.Verify(r => r.SaveOrganizationAsync(It.IsAny<Organization>()), Times.Never);
        }

        [Fact]
        public async Task CreatorGrantWithinTheCallersPowerCreatesTheOrganization()
        {
            _users.Setup(u => u.GetUserByIdAsync("caller")).ReturnsAsync(_kit.FindUser("caller")!);
            _users.Setup(u => u.UpdateUserAsync(It.IsAny<User>())).ReturnsAsync(true);

            var result = await Create().CreateOrganizationAsync(new CreateOrganizationRequest
            {
                Name = "New",
                CreatedFrom = CreatedFrom.ConstructPortal,
                DefaultRoleForMembers = ["manager"]
            });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task CloudOrganizationCreationGrantsNothingAndIsNotChecked()
        {
            var result = await Create().CreateOrganizationAsync(new CreateOrganizationRequest
            {
                Name = "New",
                CreatedFrom = CreatedFrom.Cloud,
                DefaultRoleForMembers = ["owner"]
            });

            result.IsSuccess.Should().BeTrue();
        }

        // ---------- organizations/config (R6) ----------

        [Fact]
        public async Task OrganizationConfigBelongsToTheSystemUser()
        {
            ShouldBeRefused(await Create().SaveOrganizationConfigAsync(new SaveOrganizationConfigRequest()));
            _repo.Verify(r => r.SaveOrganizationConfig(It.IsAny<IamTenantConfiguration>()), Times.Never);

            DelegationTestKit.SignIn("caller", "default");
            _repo.Setup(r => r.SaveOrganizationConfig(It.IsAny<IamTenantConfiguration>())).Returns(Task.CompletedTask);
            (await Create().SaveOrganizationConfigAsync(new SaveOrganizationConfigRequest())).IsSuccess.Should().BeTrue();
        }

        // ---------- single-organization / system user: unchanged ----------

        [Fact]
        public async Task SystemUserRoleEditsAreUnchanged()
        {
            _kit.WithStandardTree("default");
            DelegationTestKit.SignIn("caller", "default");
            _repo.Setup(r => r.GetRoleBySlugAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string slug, string? _) => _kit.RolesOf("default").FirstOrDefault(r => r.Slug == slug)!);

            var update = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = RoleNamed("owner", "default").ItemId, Name = "Owner", ParentRoleSlug = null });
            var assign = await Create().SetRolesAsync(new SetRolesRequest { Slug = "owner", AddPermissions = ["anything"] });

            update.IsSuccess.Should().BeTrue();
            assign.Success.Should().BeTrue();
            _kit.Repository.Verify(r => r.GetUserAsync(It.IsAny<string>()), Times.Never);
        }
    }
}
