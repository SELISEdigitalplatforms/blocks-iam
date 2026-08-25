using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    /// <summary>
    /// SPEC10 — organization-scope hardening. Covers H1-H7 and C1-C8: the signed organization
    /// claim decides which organization a write targets, the target must still be one the caller
    /// can act in, propagation reaches copies only, the previews agree with the write, and the
    /// role-hierarchy walk terminates.
    /// </summary>
    public class OrganizationScopeHardeningTests : IDisposable
    {
        private const string OrgA = "org-a";
        private const string OrgB = "org-b";

        private readonly Mock<IResourceRepository> _repo = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IValidator<CreatePermissionRequest>> _permValidator = new();
        private readonly Mock<IValidator<UpdatePermissionRequest>> _updatePermValidator = new();
        private readonly Mock<IValidator<CreateRoleRequest>> _roleValidator = new();
        private readonly Mock<ITenantPermissionPropagator> _propagator = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();

        public OrganizationScopeHardeningTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();

            _roleValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.InsertRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);

            // Every organization used here exists and is enabled unless a test says otherwise.
            _repo.Setup(r => r.GetOrganizationById(It.IsAny<string>()))
                .ReturnsAsync((string id) => new Organization { ItemId = id, Name = id, IsDisabled = false });
        }

        private static void InstallContext(string orgId = "default")
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

        private static SetRolesRequest SetReq(string slug = "support", string? orgId = null) => new()
        {
            Slug = slug,
            OrganizationId = orgId,
            AddPermissions = new List<string> { "p1" },
            RemovePermissions = new List<string>()
        };

        private void GivenRoleExistsIn(string organizationId, string slug = "support")
        {
            _repo.Setup(r => r.GetRoleBySlugAsync(slug, organizationId))
                .ReturnsAsync(new Role { ItemId = "r-" + organizationId, Slug = slug, Name = "S", OrganizationId = organizationId });
        }

        // ---------- ResourceWriteOrganizationScope, the pure rule (H1-H3, C1) ----------

        [Fact]
        public void Scope_NonDefaultCaller_IsPinnedToItsOwnOrganization_AndDiscardsTheRequestedId()
        {
            var scope = ResourceWriteOrganizationScope.Resolve(OrgA, OrgB);

            scope.Kind.Should().Be(ResourceWriteScopeKind.Organization);
            scope.OrganizationId.Should().Be(OrgA);
        }

        [Fact]
        public void Scope_DefaultCaller_MayNameTheTarget()
        {
            ResourceWriteOrganizationScope.Resolve("default", OrgB).OrganizationId.Should().Be(OrgB);
        }

        [Fact]
        public void Scope_DefaultCaller_NamingNothing_TargetsDefault()
        {
            ResourceWriteOrganizationScope.Resolve("default", null).OrganizationId.Should().Be("default");
            ResourceWriteOrganizationScope.Resolve("default", "   ").OrganizationId.Should().Be("default");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Scope_NoTokenOrganization_IsDenied_AndNeverCollapsesToDefault(string? tokenOrganizationId)
        {
            var scope = ResourceWriteOrganizationScope.Resolve(tokenOrganizationId, OrgB);

            scope.Kind.Should().Be(ResourceWriteScopeKind.Denied);
            scope.OrganizationId.Should().BeEmpty();
        }

        // ---------- SetRolesAsync honours the rule (H1, H2, C1-C3) ----------

        [Fact]
        public async Task SetRoles_OrgACallerNamingOrgB_WritesToOrgA()
        {
            InstallContext(OrgA);
            GivenRoleExistsIn(OrgA);

            var result = await Create().SetRolesAsync(SetReq(orgId: OrgB));

            result.Success.Should().BeTrue();
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("support", It.IsAny<List<string>>(), OrgA), Times.Once);
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("support", It.IsAny<List<string>>(), OrgB), Times.Never);
            _repo.Verify(r => r.GetRoleBySlugAsync("support", OrgB), Times.Never);
        }

        [Fact]
        public async Task SetRoles_DefaultCallerNamingOrgB_WritesToOrgB()
        {
            GivenRoleExistsIn(OrgB);

            var result = await Create().SetRolesAsync(SetReq(orgId: OrgB));

            result.Success.Should().BeTrue();
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("support", It.IsAny<List<string>>(), OrgB), Times.Once);
        }

        [Fact]
        public async Task SetRoles_NoOrganizationClaim_IsDenied()
        {
            InstallContext(string.Empty);

            var result = await Create().SetRolesAsync(SetReq(orgId: OrgB));

            result.Success.Should().BeFalse();
            result.Errors.Should().ContainKey("unauthorized");
            result.Errors["unauthorized"].Should().Be("Organization_Not_Resolved");
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SetRoles_TargetOrganizationDeleted_IsDenied()
        {
            InstallContext(OrgA);
            GivenRoleExistsIn(OrgA);
            _repo.Setup(r => r.GetOrganizationById(OrgA)).ReturnsAsync((Organization)null!);

            var result = await Create().SetRolesAsync(SetReq());

            result.Errors["forbidden"].Should().Be("Organization_Not_Found");
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SetRoles_TargetOrganizationDisabled_IsDenied()
        {
            InstallContext(OrgA);
            GivenRoleExistsIn(OrgA);
            _repo.Setup(r => r.GetOrganizationById(OrgA))
                .ReturnsAsync(new Organization { ItemId = OrgA, Name = "A", IsDisabled = true });

            var result = await Create().SetRolesAsync(SetReq());

            result.Errors["forbidden"].Should().Be("Organization_Disabled");
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SetRoles_DefaultScope_NeverLooksUpAnOrganizationDocument()
        {
            GivenRoleExistsIn("default");

            var result = await Create().SetRolesAsync(SetReq());

            result.Success.Should().BeTrue();
            _repo.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SetRoles_OrganizationLookupThrows_DeniesRatherThanProceeding()
        {
            InstallContext(OrgA);
            GivenRoleExistsIn(OrgA);
            _repo.Setup(r => r.GetOrganizationById(OrgA)).ThrowsAsync(new TimeoutException("mongo down"));

            var act = () => Create().SetRolesAsync(SetReq());

            await act.Should().ThrowAsync<TimeoutException>();
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- Propagation reaches copies only (H4, C4) ----------

        private ResourceSetToPermissionMutationEvent PropagationEvent() => new()
        {
            Entity = ResourceEntity.Role,
            Slug = "manager",
            OrganizationId = "default",
            AddPermissions = new List<string> { "p-default" },
            RemovePermissions = new List<string>(),
            PropagateToAllOrganizations = true
        };

        private void GivenTwoOrganizationsHoldingManager(bool orgBIsACopy)
        {
            _repo.Setup(r => r.GetAllOrgIdsAsync()).ReturnsAsync(new List<string> { OrgA, OrgB });
            _repo.Setup(r => r.GetPermissionsByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new List<Permission> { new() { ItemId = "p-default", Resource = "reports::export" } });
            _repo.Setup(r => r.GetPermissionsByResourcesAsync(It.IsAny<List<string>>(), It.IsAny<string>()))
                .ReturnsAsync((List<string> _, string org) =>
                    new List<Permission> { new() { ItemId = "p-" + org, Resource = "reports::export", OrganizationId = org } });

            _repo.Setup(r => r.GetRoleBySlugAsync("manager", OrgA))
                .ReturnsAsync(new Role { ItemId = "r-a", Slug = "manager", OrganizationId = OrgA, CreatedFromDefault = true });
            _repo.Setup(r => r.GetRoleBySlugAsync("manager", OrgB))
                .ReturnsAsync(new Role { ItemId = "r-b", Slug = "manager", OrganizationId = OrgB, CreatedFromDefault = orgBIsACopy });
        }

        [Fact]
        public async Task Propagation_ReachesCopies()
        {
            GivenTwoOrganizationsHoldingManager(orgBIsACopy: true);

            await Create().ProcessPermissionAsync(PropagationEvent());

            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), OrgA), Times.Once);
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), OrgB), Times.Once);
        }

        [Fact]
        public async Task Propagation_SkipsAnOrganizationsOwnRoleUnderTheSameSlug()
        {
            GivenTwoOrganizationsHoldingManager(orgBIsACopy: false);

            await Create().ProcessPermissionAsync(PropagationEvent());

            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), OrgA), Times.Once);
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), OrgB), Times.Never);
        }

        // ---------- The hierarchy walk terminates (H6, C5, C6) ----------

        private static CreateRoleRequest RoleReq(string slug, string? parent) => new()
        {
            Name = "R", Slug = slug, Description = "d", ParentRoleSlug = parent
        };

        [Fact]
        public async Task CreateRole_TwoLevelParentChain_StoresTheWholeChainAndReturns()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("lead"))
                .ReturnsAsync(new Role { Slug = "lead", Name = "L", ParentRoleSlug = "manager" });
            _repo.Setup(r => r.GetRoleBySlugAsync("manager"))
                .ReturnsAsync(new Role { Slug = "manager", Name = "M", ParentRoleSlug = null });

            var result = await Create().CreateRoleAsync(RoleReq("junior", "lead"));

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x =>
                x.AncestorRoleSlugs.Count == 2 &&
                x.AncestorRoleSlugs[0] == "lead" &&
                x.AncestorRoleSlugs[1] == "manager")), Times.Once);
        }

        [Fact]
        public async Task CreateRole_ParentAbsentFromTheCallersOrganization_IsAValidationErrorNotAnException()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("ghost")).ReturnsAsync((Role)null!);

            var result = await Create().CreateRoleAsync(RoleReq("junior", "ghost"));

            result.IsSuccess.Should().BeFalse();
            result.Errors["ParentRoleSlug"].Should().Be("Parent_Role_Not_Found");
            _repo.Verify(r => r.InsertRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task CreateRole_CyclicParentChain_IsRefusedRatherThanLoopingForever()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("a"))
                .ReturnsAsync(new Role { Slug = "a", Name = "A", ParentRoleSlug = "b" });
            _repo.Setup(r => r.GetRoleBySlugAsync("b"))
                .ReturnsAsync(new Role { Slug = "b", Name = "B", ParentRoleSlug = "a" });

            var result = await Create().CreateRoleAsync(RoleReq("junior", "a"));

            result.Errors["ParentRoleSlug"].Should().Be("Role_Hierarchy_Cycle_Detected");
            _repo.Verify(r => r.InsertRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task CreateRole_SelfParentingChain_IsRefused()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("loop"))
                .ReturnsAsync(new Role { Slug = "loop", Name = "L", ParentRoleSlug = "loop" });

            var result = await Create().CreateRoleAsync(RoleReq("junior", "loop"));

            result.Errors["ParentRoleSlug"].Should().Be("Role_Hierarchy_Cycle_Detected");
        }

        [Fact]
        public async Task UpdateRole_ParentAbsent_IsAValidationErrorAndWritesNothing()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1"))
                .ReturnsAsync(new Role { ItemId = "r1", Slug = "junior", Name = "J", OrganizationId = "default" });
            _repo.Setup(r => r.GetRoleBySlugAsync("ghost")).ReturnsAsync((Role)null!);

            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest
            {
                ItemId = "r1", Name = "Junior", ParentRoleSlug = "ghost"
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors["ParentRoleSlug"].Should().Be("Parent_Role_Not_Found");
            _repo.Verify(r => r.UpdateRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task UpdateRole_TwoLevelParentChain_StoresTheWholeChain()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1"))
                .ReturnsAsync(new Role { ItemId = "r1", Slug = "junior", Name = "J", OrganizationId = "default" });
            _repo.Setup(r => r.GetRoleBySlugAsync("lead"))
                .ReturnsAsync(new Role { Slug = "lead", Name = "L", ParentRoleSlug = "manager" });
            _repo.Setup(r => r.GetRoleBySlugAsync("manager"))
                .ReturnsAsync(new Role { Slug = "manager", Name = "M", ParentRoleSlug = null });

            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest
            {
                ItemId = "r1", Name = "Junior", ParentRoleSlug = "lead"
            });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdateRoleAsync(It.Is<Role>(x =>
                x.AncestorRoleSlugs.Count == 2 && x.AncestorRoleSlugs[1] == "manager")), Times.Once);
        }

        // ---------- Multi-org off leaves everything as it was (C7) ----------

        [Fact]
        public async Task Propagation_MultiOrgDisabled_TouchesNoOtherOrganization()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = false });
            GivenTwoOrganizationsHoldingManager(orgBIsACopy: true);

            await Create().ProcessPermissionAsync(PropagationEvent());

            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), OrgA), Times.Never);
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), OrgB), Times.Never);
        }
    }
}
