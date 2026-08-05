using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    // Extra coverage for ResourceMutationService branches NOT covered by
    // ResourceMutationServiceTests: PropagateSetPermissionsAsync (via ProcessPermissionAsync),
    // the CopyPermissionsFromDefault batch loop (via ExecuteOrganizationProvisioningAsync),
    // and SetRolesAsync with propagation enabled.
    public class ResourceMutationServiceExtraTests : IDisposable
    {
        private readonly Mock<IResourceRepository> _repo = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IValidator<CreatePermissionRequest>> _permValidator = new();
        private readonly Mock<IValidator<UpdatePermissionRequest>> _updatePermValidator = new();
        private readonly Mock<IValidator<CreateRoleRequest>> _roleValidator = new();
        private readonly Mock<ITenantPermissionPropagator> _propagator = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();

        public ResourceMutationServiceExtraTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();
            _permValidator.Setup(v => v.ValidateAsync(It.IsAny<CreatePermissionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _updatePermValidator.Setup(v => v.ValidateAsync(It.IsAny<UpdatePermissionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _roleValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration());
            _iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
        }

        private static void InstallContext(string userId = "actor-1", string orgId = "default")
        {
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

        private ResourceMutationService Create() =>
            new(NullLogger<ResourceMutationService>.Instance, _repo.Object, _iam.Object,
                _permValidator.Object, _updatePermValidator.Object, _roleValidator.Object,
                _propagator.Object, _activity.Object);


        [Fact]
        public async Task ExecuteOrganizationProvisioning_CopiesPermissionsInFullThenEmptyBatch()
        {
            _repo.Setup(r => r.GetRolesByOrgAsync("default")).ReturnsAsync(new List<Role>());

            var fullPage = Enumerable.Range(0, 100)
                .Select(i => new Permission { ItemId = "p" + i, Name = "P" + i, Resource = "res" + i })
                .ToList();
            _repo.SetupSequence(r => r.GetPermissionsByOrgAsync("default", It.IsAny<int?>(), It.IsAny<int?>()))
                .ReturnsAsync(fullPage)
                .ReturnsAsync(new List<Permission>());
            _repo.Setup(r => r.InsertPermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);

            await Create().ExecuteOrganizationProvisioningAsync(new OrganizationProvisioningEvent { OrganizationId = "org-42", UserId = "u1" });

            _repo.Verify(r => r.GetPermissionsByOrgAsync("default", It.IsAny<int?>(), It.IsAny<int?>()), Times.Exactly(2));
            _repo.Verify(r => r.InsertPermissionsAsync(It.Is<List<Permission>>(l => l.Count == 100 && l.All(p => p.OrganizationId == "org-42" && p.LastUpdatedBy == "u1"))), Times.Once);
        }

        // ---------- ExecutePropagationRolePermissionUpdateAsync: ForAllOrg edge branches ----------

        private static PropagationRolePermissionUpdateEvent Prop(string entity, string action, string itemId) =>
            new() { Entity = entity, Action = action, ItemId = itemId };

        [Fact]
        public async Task Propagation_PermissionInsert_PermissionNotFound_DoesNotInsert()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync((Permission)null!);

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("permission", "insert", "p1"));

            _repo.Verify(r => r.InsertPermissionsAsync(It.IsAny<List<Permission>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_PermissionUpdate_PermissionNotFound_DoesNotUpdate()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync((Permission)null!);

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("permission", "update", "p1"));

            _repo.Verify(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_PermissionUpdate_NoOrgPermissions_DoesNotUpdate()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Resource = "res" });
            _repo.Setup(r => r.GetPermissionsByResourceAsync("res")).ReturnsAsync(new List<Permission>());

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("permission", "update", "p1"));

            _repo.Verify(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_PermissionUpdate_SkipsDefaultOrgAndUpdatesOthers()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1"))
                .ReturnsAsync(new Permission { ItemId = "p1", Resource = "res", Name = "New" });
            _repo.Setup(r => r.GetPermissionsByResourceAsync("res")).ReturnsAsync(new List<Permission>
            {
                new() { ItemId = "def", Resource = "res", Name = "Old", OrganizationId = "default" },
                new() { ItemId = "o1p", Resource = "res", Name = "Old", OrganizationId = "o1" }
            });
            _repo.Setup(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("permission", "update", "p1"));

            _repo.Verify(r => r.UpdatePermissionsAsync(It.Is<List<Permission>>(
                l => l.Single(p => p.OrganizationId == "o1").Name == "New"
                     && l.Single(p => p.OrganizationId == "default").Name == "Old")), Times.Once);
        }

        [Fact]
        public async Task Propagation_PermissionDelete_PermissionNotFound_DoesNotUpdate()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync((Permission)null!);

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("permission", "delete", "p1"));

            _repo.Verify(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_PermissionDelete_AllDefaultOrAlreadyArchived_DoesNotUpdate()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Resource = "res" });
            _repo.Setup(r => r.GetPermissionsByResourceAsync("res")).ReturnsAsync(new List<Permission>
            {
                new() { ItemId = "def", Resource = "res", OrganizationId = "default" },
                new() { ItemId = "o1p", Resource = "res", OrganizationId = "o1", IsArchived = true }
            });

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("permission", "delete", "p1"));

            _repo.Verify(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_RoleInsert_RoleNotFound_DoesNotInsert()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync((Role)null!);

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "insert", "r1"));

            _repo.Verify(r => r.InsertRolesAsync(It.IsAny<List<Role>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_RoleInsert_NoOrganizations_DoesNotInsert()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Slug = "admin", Name = "A" });
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .ReturnsAsync(new GetOrganizationsResponse { Organizations = new List<Organization>() });

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "insert", "r1"));

            _repo.Verify(r => r.InsertRolesAsync(It.IsAny<List<Role>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_RoleInsert_AllOrgsAlreadyHaveRole_DoesNotInsert()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Slug = "admin", Name = "A" });
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .ReturnsAsync(new GetOrganizationsResponse { Organizations = new List<Organization> { new() { ItemId = "o1", Name = "O1" } } });
            _repo.Setup(r => r.GetRoleBySlugAsync("admin", "o1")).ReturnsAsync(new Role { ItemId = "existing", Slug = "admin", Name = "A", OrganizationId = "o1" });

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "insert", "r1"));

            _repo.Verify(r => r.InsertRolesAsync(It.IsAny<List<Role>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_RoleUpdate_RoleNotFound_DoesNotUpdate()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync((Role)null!);

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "update", "r1"));

            _repo.Verify(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_RoleUpdate_NoRolesForSlug_DoesNotUpdate()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Slug = "admin", Name = "A" });
            _repo.Setup(r => r.GetRolesBySlugAsync("admin")).ReturnsAsync(new List<Role>());

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "update", "r1"));

            _repo.Verify(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_RoleUpdate_NoDefaultCreatedOrgRoles_DoesNotUpdate()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Slug = "admin", Name = "A" });
            _repo.Setup(r => r.GetRolesBySlugAsync("admin")).ReturnsAsync(new List<Role>
            {
                new() { ItemId = "r1", Slug = "admin", Name = "A" },                       // the source itself
                new() { ItemId = "r2", Slug = "admin", Name = "B", CreatedFromDefault = false } // not default-created
            });

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "update", "r1"));

            _repo.Verify(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_RoleDelete_RoleNotFound_DoesNothing()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync((Role)null!);

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "delete", "r1"));

            _repo.Verify(r => r.GetRolesBySlugAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_RoleDelete_NoRolesForSlug_DoesNothing()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Slug = "admin", Name = "A" });
            _repo.Setup(r => r.GetRolesBySlugAsync("admin")).ReturnsAsync(new List<Role>());

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "delete", "r1"));

            _repo.Verify(r => r.GetRolesBySlugAsync("admin"), Times.Once);
        }

        [Fact]
        public async Task Propagation_RoleDelete_OnlySourceRole_NoOrphaned()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Slug = "admin", Name = "A" });
            _repo.Setup(r => r.GetRolesBySlugAsync("admin")).ReturnsAsync(new List<Role>
            {
                new() { ItemId = "r1", Slug = "admin", Name = "A", CreatedFromDefault = true }
            });

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "delete", "r1"));

            _repo.Verify(r => r.GetRolesBySlugAsync("admin"), Times.Once);
        }

        [Fact]
        public async Task Propagation_RoleDelete_OrphanedRolesPresent_LogsAndCompletes()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Slug = "admin", Name = "A" });
            _repo.Setup(r => r.GetRolesBySlugAsync("admin")).ReturnsAsync(new List<Role>
            {
                new() { ItemId = "r1", Slug = "admin", Name = "A" },
                new() { ItemId = "r2", Slug = "admin", Name = "A", CreatedFromDefault = true, OrganizationId = "o1" }
            });

            await Create().ExecutePropagationRolePermissionUpdateAsync(Prop("role", "delete", "r1"));

            _repo.Verify(r => r.GetRolesBySlugAsync("admin"), Times.Once);
        }

        // ---------- GetOrganizationAsync / GetMyOrganizationAsync ----------

        [Fact]
        public async Task GetOrganization_MultiOrgDisabled_ReturnsError()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = false });

            var result = await Create().GetOrganizationAsync("org-1");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("multi_org_disabled");
        }

        [Fact]
        public async Task GetOrganization_EmptyId_ReturnsInvalidRequest()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            var result = await Create().GetOrganizationAsync("   ");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task GetOrganization_HappyPath_ReturnsOrganization()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.GetOrganizationById("org-1")).ReturnsAsync(new Organization { ItemId = "org-1", Name = "Acme" });

            var result = await Create().GetOrganizationAsync("org-1");

            result.IsSuccess.Should().BeTrue();
            result.Organization!.ItemId.Should().Be("org-1");
        }

        [Fact]
        public async Task GetMyOrganization_MultiOrgDisabled_ReturnsError()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = false });

            var result = await Create().GetMyOrganizationAsync();

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("multi_org_disabled");
        }

        [Fact]
        public async Task GetMyOrganization_NoUserContext_ReturnsInvalidRequest()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            BlocksContext.SetContext(null);

            var result = await Create().GetMyOrganizationAsync();

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task GetMyOrganization_NoMemberships_ReturnsEmptyList()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.GetOrganizationIdsByUserIdAsync("actor-1")).ReturnsAsync(new List<string>());

            var result = await Create().GetMyOrganizationAsync();

            result.IsSuccess.Should().BeTrue();
            result.Organizations.Should().BeEmpty();
        }

        [Fact]
        public async Task GetMyOrganization_WithMemberships_ReturnsOrderedList()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.GetOrganizationIdsByUserIdAsync("actor-1")).ReturnsAsync(new List<string> { "o2", "o1" });
            _repo.Setup(r => r.GetOrganizationsByIdsAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<Organization>
            {
                new() { ItemId = "o1", Name = "One" },
                new() { ItemId = "o2", Name = "Two" }
            });

            var result = await Create().GetMyOrganizationAsync();

            result.IsSuccess.Should().BeTrue();
            result.Organizations.Should().HaveCount(2);
            result.Organizations[0].ItemId.Should().Be("o2");
            result.Organizations[1].ItemId.Should().Be("o1");
        }
    }
}
