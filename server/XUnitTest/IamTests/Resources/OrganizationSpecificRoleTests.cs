using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    /// <summary>
    /// SPEC11 — organization-specific role creation. Covers H1-H7 and C1-C8: a child organization
    /// may create a role of its own, it is stored under a slug carrying an organization fragment,
    /// it propagates nowhere, and role names are unique among the roles an organization owns.
    /// </summary>
    public class OrganizationSpecificRoleTests : IDisposable
    {
        // First 8 hex of the fragment source is "f47ac10b" once the dashes are stripped.
        private const string OrgA = "f47ac10b-58cc-4372-a567-0e02b2c3d479";
        private const string OrgAFragment = "f47ac10b";

        private readonly Mock<IResourceRepository> _repo = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IValidator<CreatePermissionRequest>> _permValidator = new();
        private readonly Mock<IValidator<UpdatePermissionRequest>> _updatePermValidator = new();
        private readonly Mock<IValidator<CreateRoleRequest>> _roleValidator = new();
        private readonly Mock<ITenantPermissionPropagator> _propagator = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();

        public OrganizationSpecificRoleTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext(OrgA);

            _roleValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.InsertRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _repo.Setup(r => r.GetOrganizationById(It.IsAny<string>()))
                .ReturnsAsync((string id) => new Organization { ItemId = id, Name = "O", IsDisabled = false });
            _repo.Setup(r => r.GetRolesBySlugAsync(It.IsAny<string>())).ReturnsAsync(new List<Role>());
            _repo.Setup(r => r.HasOwnedRoleWithNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
        }

        private static void InstallContext(string orgId)
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
            GC.SuppressFinalize(this);
        }

        private ResourceMutationService Create() =>
            new(NullLogger<ResourceMutationService>.Instance, _repo.Object, _iam.Object,
                _permValidator.Object, _updatePermValidator.Object, _roleValidator.Object,
                _propagator.Object, _activity.Object);

        private static CreateRoleRequest RoleReq(string name = "Regional Manager", string slug = "manager") => new()
        {
            Name = name, Slug = slug, Description = "d"
        };

        // ---------- H1, H2, H3: an organization's own role ----------

        [Fact]
        public async Task CreateRole_FromChildOrg_IsStampedWithTheCallersOrganization()
        {
            var result = await Create().CreateRoleAsync(RoleReq());

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x =>
                x.OrganizationId == OrgA && !x.CreatedFromDefault)), Times.Once);
        }

        [Fact]
        public async Task CreateRole_FromChildOrg_AppendsTheOrganizationFragmentToTheSlug()
        {
            await Create().CreateRoleAsync(RoleReq());

            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x => x.Slug == $"manager_{OrgAFragment}")), Times.Once);
        }

        [Fact]
        public async Task CreateRole_FromChildOrg_QueuesNoPropagation()
        {
            await Create().CreateRoleAsync(RoleReq());

            _iam.Verify(i => i.SendToQueueAsync(
                It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Never);
        }

        // ---------- H4: the default organization is unchanged ----------

        [Fact]
        public async Task CreateRole_FromDefaultOrg_KeepsABareSlugAndStillPropagates()
        {
            InstallContext("default");

            var result = await Create().CreateRoleAsync(RoleReq("Auditor", "auditor"));

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x => x.Slug == "auditor")), Times.Once);
            _iam.Verify(i => i.SendToQueueAsync(
                It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Once);
        }

        [Fact]
        public async Task CreateRole_FromDefaultOrg_NeverLooksUpAnOrganizationDocument()
        {
            InstallContext("default");

            await Create().CreateRoleAsync(RoleReq("Auditor", "auditor"));

            _repo.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
        }

        // ---------- H5: parent references are never suffixed ----------

        [Fact]
        public async Task CreateRole_WithParent_StoresTheParentSlugVerbatim()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync($"manager_{OrgAFragment}"))
                .ReturnsAsync(new Role { Slug = $"manager_{OrgAFragment}", Name = "M", ParentRoleSlug = null });

            var request = RoleReq("Team Lead", "lead");
            request.ParentRoleSlug = $"manager_{OrgAFragment}";

            var result = await Create().CreateRoleAsync(request);

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x =>
                x.Slug == $"lead_{OrgAFragment}"
                && x.ParentRoleSlug == $"manager_{OrgAFragment}"
                && x.AncestorRoleSlugs.Count == 1
                && x.AncestorRoleSlugs[0] == $"manager_{OrgAFragment}")), Times.Once);
        }

        // ---------- H6, C7: a default copy and an own role may share a name ----------

        [Fact]
        public async Task CreateRole_NameHeldOnlyByADefaultDerivedCopy_IsAllowed()
        {
            // HasOwnedRoleWithNameAsync excludes CreatedFromDefault copies, so the repository
            // reports no conflict and the create proceeds.
            _repo.Setup(r => r.HasOwnedRoleWithNameAsync("Manager", OrgA, null)).ReturnsAsync(false);

            var result = await Create().CreateRoleAsync(RoleReq("Manager", "manager"));

            result.IsSuccess.Should().BeTrue();
        }

        // ---------- C1: name uniqueness among the roles the organization owns ----------

        [Fact]
        public async Task CreateRole_NameAlreadyOwnedByTheOrganization_IsRefused()
        {
            _repo.Setup(r => r.HasOwnedRoleWithNameAsync("Regional Manager", OrgA, null)).ReturnsAsync(true);

            var result = await Create().CreateRoleAsync(RoleReq());

            result.IsSuccess.Should().BeFalse();
            result.Errors["Name"].Should().Be("Role_Name_Already_Exists_In_Organization");
            _repo.Verify(r => r.InsertRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task CreateRole_DuplicateName_IsRefusedBeforeAnySlugWork()
        {
            _repo.Setup(r => r.HasOwnedRoleWithNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            await Create().CreateRoleAsync(RoleReq());

            _repo.Verify(r => r.GetRolesBySlugAsync(It.IsAny<string>()), Times.Never);
        }

        // ---------- C2: slug availability, own-organization vs another ----------

        [Fact]
        public async Task CreateRole_SlugHeldByAnArchivedRoleInTheSameOrganization_IsRefused()
        {
            _repo.Setup(r => r.GetRolesBySlugAsync($"manager_{OrgAFragment}"))
                .ReturnsAsync(new List<Role>
                {
                    new() { ItemId = "old", Slug = $"manager_{OrgAFragment}", OrganizationId = OrgA, IsArchived = true }
                });

            var result = await Create().CreateRoleAsync(RoleReq());

            result.Errors["Slug"].Should().Be("Role_Slug_Already_In_Use_Including_Archived_Roles");
            _repo.Verify(r => r.InsertRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task CreateRole_FragmentCollidesWithAnotherOrganization_LengthensTheFragment()
        {
            _repo.Setup(r => r.GetRolesBySlugAsync($"manager_{OrgAFragment}"))
                .ReturnsAsync(new List<Role>
                {
                    new() { ItemId = "other", Slug = $"manager_{OrgAFragment}", OrganizationId = "some-other-org" }
                });

            var result = await Create().CreateRoleAsync(RoleReq());

            result.IsSuccess.Should().BeTrue();
            // 8 hex was taken by another organization, so the next candidate is 12.
            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x => x.Slug == "manager_f47ac10b58cc")), Times.Once);
        }

        [Fact]
        public async Task CreateRole_DefaultOrgSlugAlreadyTaken_IsRefused()
        {
            InstallContext("default");
            _repo.Setup(r => r.GetRolesBySlugAsync("auditor"))
                .ReturnsAsync(new List<Role> { new() { ItemId = "x", Slug = "auditor", OrganizationId = "default" } });

            var result = await Create().CreateRoleAsync(RoleReq("Auditor", "auditor"));

            result.Errors["Slug"].Should().Be("Role_Slug_Already_In_Use_Including_Archived_Roles");
        }

        // ---------- C3: multi-org is required for a non-default create ----------

        [Fact]
        public async Task CreateRole_FromChildOrg_MultiOrgDisabled_IsRefused()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = false });

            var result = await Create().CreateRoleAsync(RoleReq());

            result.Errors["forbidden"].Should().Be("Multi_Org_Required_For_Organization_Role");
            _repo.Verify(r => r.InsertRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        // ---------- C4: the organization must exist and be enabled ----------

        [Fact]
        public async Task CreateRole_OrganizationDeleted_IsRefused()
        {
            _repo.Setup(r => r.GetOrganizationById(OrgA)).ReturnsAsync((Organization)null!);

            var result = await Create().CreateRoleAsync(RoleReq());

            result.Errors["forbidden"].Should().Be("Organization_Not_Found");
        }

        [Fact]
        public async Task CreateRole_OrganizationDisabled_IsRefused()
        {
            _repo.Setup(r => r.GetOrganizationById(OrgA))
                .ReturnsAsync(new Organization { ItemId = OrgA, Name = "A", IsDisabled = true });

            var result = await Create().CreateRoleAsync(RoleReq());

            result.Errors["forbidden"].Should().Be("Organization_Disabled");
        }

        [Fact]
        public async Task CreateRole_NoOrganizationClaim_IsRefused()
        {
            InstallContext(string.Empty);

            var result = await Create().CreateRoleAsync(RoleReq());

            result.Errors["unauthorized"].Should().Be("Organization_Not_Resolved");
        }

        // ---------- C5: a hand-crafted suffix cannot impersonate another organization ----------

        [Fact]
        public async Task CreateRole_SlugAlreadyCarryingAFragmentShape_IsTreatedAsTheBase()
        {
            var result = await Create().CreateRoleAsync(RoleReq("Sneaky", "auditor_deadbeef"));

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x =>
                x.Slug == $"auditor_deadbeef_{OrgAFragment}" && x.OrganizationId == OrgA)), Times.Once);
        }

        // ---------- C6: format validation runs before the uniqueness lookup ----------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task CreateRole_BlankName_DoesNotQueryNameUniqueness(string? name)
        {
            _roleValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Name", "req") }));

            var result = await Create().CreateRoleAsync(RoleReq(name!, "x"));

            result.IsSuccess.Should().BeFalse();
            _repo.Verify(r => r.HasOwnedRoleWithNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- H7: update applies the same rule, and never touches the slug ----------

        [Fact]
        public async Task UpdateRole_RenameOntoANameTheOrganizationOwns_IsRefused()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role
            {
                ItemId = "r1", Slug = $"lead_{OrgAFragment}", Name = "Team Lead", OrganizationId = OrgA
            });
            _repo.Setup(r => r.HasOwnedRoleWithNameAsync("Regional Manager", OrgA, "r1")).ReturnsAsync(true);

            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest
            {
                ItemId = "r1", Name = "Regional Manager"
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors["Name"].Should().Be("Role_Name_Already_Exists_In_Organization");
            _repo.Verify(r => r.UpdateRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task UpdateRole_KeepingItsOwnName_IsNotAConflictWithItself()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role
            {
                ItemId = "r1", Slug = $"lead_{OrgAFragment}", Name = "Team Lead", OrganizationId = OrgA
            });

            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest
            {
                ItemId = "r1", Name = "Team Lead", Description = "changed"
            });

            result.IsSuccess.Should().BeTrue();
            // Excluding self is the repository's job; the service must pass the id through.
            _repo.Verify(r => r.HasOwnedRoleWithNameAsync("Team Lead", OrgA, "r1"), Times.Once);
        }

        [Fact]
        public async Task UpdateRole_LeavesTheSlugUntouched()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role
            {
                ItemId = "r1", Slug = $"lead_{OrgAFragment}", Name = "Team Lead", OrganizationId = OrgA
            });

            await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "r1", Name = "Squad Lead" });

            _repo.Verify(r => r.UpdateRoleAsync(It.Is<Role>(x => x.Slug == $"lead_{OrgAFragment}")), Times.Once);
        }
    }
}
