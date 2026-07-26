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
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    public class ResourceMutationServiceTests : IDisposable
    {
        private readonly Mock<IResourceRepository> _repo = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IValidator<CreatePermissionRequest>> _permValidator = new();
        private readonly Mock<IValidator<UpdatePermissionRequest>> _updatePermValidator = new();
        private readonly Mock<IValidator<CreateRoleRequest>> _roleValidator = new();
        private readonly Mock<ITenantPermissionPropagator> _propagator = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();

        public ResourceMutationServiceTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();
            _permValidator.Setup(v => v.ValidateAsync(It.IsAny<CreatePermissionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _updatePermValidator.Setup(v => v.ValidateAsync(It.IsAny<UpdatePermissionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _roleValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration());
            _repo.Setup(r => r.InsertPermissionAsync(It.IsAny<Permission>())).ReturnsAsync(true);
            _repo.Setup(r => r.InsertRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdatePermissionAsync(It.IsAny<Permission>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateAllSamePermissionAsync(It.IsAny<Permission>())).ReturnsAsync(true);
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

        private static CreatePermissionRequest PermReq() => new()
        {
            Name = "Read Users", Resource = "USERS_READ", Description = "d", ResourceGroup = "users"
        };

        private static CreateRoleRequest RoleReq(string slug = "admin", string? parent = null) => new()
        {
            Name = "Admin", Slug = slug, ParentRoleSlug = parent, Description = "d"
        };

        // ---------- CreatePermissionAsync ----------

        [Fact]
        public async Task CreatePermission_NonDefaultOrg_Forbidden()
        {
            InstallContext(orgId: "org-9");
            var result = await Create().CreatePermissionAsync(PermReq());
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("forbidden");
        }

        [Fact]
        public async Task CreatePermission_ValidationFails_ReturnsErrors()
        {
            _permValidator.Setup(v => v.ValidateAsync(It.IsAny<CreatePermissionRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Name", "req") }));
            var result = await Create().CreatePermissionAsync(PermReq());
            result.Errors.Should().ContainKey("Name");
        }

        [Fact]
        public async Task CreatePermission_HappyPath_InsertsAndSendsEvent()
        {
            var result = await Create().CreatePermissionAsync(PermReq());
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.InsertPermissionAsync(It.Is<Permission>(p => p.Resource == "users_read")), Times.Once);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task CreatePermission_MultiOrg_QueuesPropagation()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            await Create().CreatePermissionAsync(PermReq());
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Once);
        }

        // ---------- CreateRoleAsync ----------

        [Fact]
        public async Task CreateRole_NonDefaultOrg_Forbidden()
        {
            InstallContext(orgId: "org-9");
            var result = await Create().CreateRoleAsync(RoleReq());
            result.Errors.Should().ContainKey("forbidden");
        }

        [Fact]
        public async Task CreateRole_ValidationFails_ReturnsErrors()
        {
            _roleValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Slug", "req") }));
            var result = await Create().CreateRoleAsync(RoleReq());
            result.Errors.Should().ContainKey("Slug");
        }

        [Fact]
        public async Task CreateRole_HappyPath_InsertsRole()
        {
            var result = await Create().CreateRoleAsync(RoleReq());
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x => x.Slug == "admin")), Times.Once);
        }

        [Fact]
        public async Task CreateRole_WithParent_BuildsAncestorSlugs()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("manager")).ReturnsAsync(new Role { Slug = "manager", Name = "M", ParentRoleSlug = null });
            var result = await Create().CreateRoleAsync(RoleReq("lead", "manager"));
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x => x.AncestorRoleSlugs.Contains("manager"))), Times.Once);
        }

        // ---------- UpdatePermissionAsync ----------

        [Fact]
        public async Task UpdatePermission_NonDefaultOrg_Forbidden()
        {
            InstallContext(orgId: "org-2");
            var result = await Create().UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "n", Resource = "r", ResourceGroup = "g" });
            result.Errors.Should().ContainKey("forbidden");
        }

        [Fact]
        public async Task UpdatePermission_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync((Permission)null!);
            var result = await Create().UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "n", Resource = "r", ResourceGroup = "g" });
            result.Errors.Should().ContainKey("ItemId");
        }

        [Fact]
        public async Task UpdatePermission_HappyPath_Updates()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "old", Resource = "old" });
            var result = await Create().UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "New", Resource = "NEW_RES", ResourceGroup = "g" });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdatePermissionAsync(It.Is<Permission>(p => p.Resource == "new_res")), Times.Once);
            _repo.Verify(r => r.UpdateAllSamePermissionAsync(It.IsAny<Permission>()), Times.Once);
        }

        [Fact]
        public async Task UpdatePermission_Archived_SendsDeleteEvent()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "old", Resource = "old" });
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            var result = await Create().UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "n", Resource = "r", ResourceGroup = "g", IsArchived = true });
            result.IsSuccess.Should().BeTrue();
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.Is<PropagationRolePermissionUpdateEvent>(e => e.Action == "delete")), Times.Once);
        }

        [Fact]
        public async Task UpdatePermission_RepositoryFails_ReturnsUnsuccessful()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "n", Resource = "r" });
            _repo.Setup(r => r.UpdatePermissionAsync(It.IsAny<Permission>())).ReturnsAsync(false);
            var result = await Create().UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "n", Resource = "r", ResourceGroup = "g" });
            result.IsSuccess.Should().BeFalse();
        }

        // ---------- UpdateRoleAsync ----------

        [Fact]
        public async Task UpdateRole_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync((Role)null!);
            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "r1", Name = "N" });
            result.Errors.Should().ContainKey("ItemId");
        }

        [Fact]
        public async Task UpdateRole_DefaultDerivedNonDefaultOrg_Forbidden()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Name = "N", Slug = "s", CreatedFromDefault = true, OrganizationId = "org-9" });
            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "r1", Name = "N" });
            result.Errors.Should().ContainKey("forbidden");
        }

        [Fact]
        public async Task UpdateRole_EmptyName_ReturnsError()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Name = "N", Slug = "s", OrganizationId = "default" });
            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "r1", Name = "" });
            result.Errors.Should().ContainKey("Name");
        }

        [Fact]
        public async Task UpdateRole_TooLongName_ReturnsError()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Name = "N", Slug = "s", OrganizationId = "default" });
            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "r1", Name = new string('x', 151) });
            result.Errors.Should().ContainKey("Name");
        }

        [Fact]
        public async Task UpdateRole_HappyPath_Updates()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Name = "old", Slug = "s", OrganizationId = "default" });
            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "r1", Name = "New" });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdateRoleAsync(It.Is<Role>(x => x.Name == "New")), Times.Once);
        }

        [Fact]
        public async Task UpdateRole_WithParentSlug_ComputesAncestorsAndPropagatesMultiOrg()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1"))
                .ReturnsAsync(new Role { ItemId = "r1", Name = "old", Slug = "child", OrganizationId = "default" });
            // Parent role has no further parent, so the ancestor walk terminates after one hop.
            _repo.Setup(r => r.GetRoleBySlugAsync("parent", It.IsAny<string>()))
                .ReturnsAsync(new Role { ItemId = "p1", Name = "Parent", Slug = "parent", OrganizationId = "default", ParentRoleSlug = null });
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            var result = await Create().UpdateRoleAsync(
                new UpdateRoleRequest { ItemId = "r1", Name = "New", ParentRoleSlug = "Parent", CanCreateOwn = true });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdateRoleAsync(It.Is<Role>(x => x.ParentRoleSlug == "parent" && x.AncestorRoleSlugs.Contains("parent"))), Times.Once);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task UpdateRole_RepositoryFailure_ReturnsEmptyResponse()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1"))
                .ReturnsAsync(new Role { ItemId = "r1", Name = "old", Slug = "s", OrganizationId = "default" });
            _repo.Setup(r => r.UpdateRoleAsync(It.IsAny<Role>())).ReturnsAsync(false);

            var result = await Create().UpdateRoleAsync(new UpdateRoleRequest { ItemId = "r1", Name = "New" });

            result.IsSuccess.Should().BeFalse();
        }

        // ---------- SetRolesAsync ----------

        [Fact]
        public async Task SetRoles_EmptySlug_ReturnsError()
        {
            var result = await Create().SetRolesAsync(new SetRolesRequest { Slug = "" });
            result.Errors.Should().ContainKey("Slug");
        }

        [Fact]
        public async Task SetRoles_RoleNotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("admin")).ReturnsAsync((Role)null!);
            var result = await Create().SetRolesAsync(new SetRolesRequest { Slug = "admin" });
            result.Errors.Should().ContainKey("Role");
        }

        [Fact]
        public async Task SetRoles_DefaultDerived_Forbidden()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("admin")).ReturnsAsync(new Role { Slug = "admin", Name = "A", CreatedFromDefault = true });
            var result = await Create().SetRolesAsync(new SetRolesRequest { Slug = "admin" });
            result.Errors.Should().ContainKey("forbidden");
        }

        [Fact]
        public async Task SetRoles_HappyPath_AddsAndRemoves()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("admin")).ReturnsAsync(new Role { Slug = "admin", Name = "A" });
            _repo.Setup(r => r.UpdateRolePermissionByIdsAsync("admin", It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRolePermissionByIdsAsync("admin", It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);

            var result = await Create().SetRolesAsync(new SetRolesRequest
            {
                Slug = "admin",
                AddPermissions = new List<string> { "p1" },
                RemovePermissions = new List<string> { "p2" }
            });

            result.Success.Should().BeTrue();
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("admin", It.IsAny<List<string>>(), It.IsAny<string>()), Times.Once);
            _repo.Verify(r => r.RemoveRolePermissionByIdsAsync("admin", It.IsAny<List<string>>(), It.IsAny<string>()), Times.Once);
        }

        // ---------- ExecutePropagationRolePermissionUpdateAsync ----------

        [Fact]
        public async Task Propagation_NullCommand_NoThrow()
        {
            await Create().ExecutePropagationRolePermissionUpdateAsync(null!);
        }

        [Fact]
        public async Task Propagation_UnknownEntity_NoAction()
        {
            await Create().ExecutePropagationRolePermissionUpdateAsync(new PropagationRolePermissionUpdateEvent { Entity = "weird", Action = "nope", ItemId = "x" });
            _repo.Verify(r => r.GetPermissionByIdAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_PermissionInsert_MissingPermission_Handled()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync((Permission)null!);
            await Create().ExecutePropagationRolePermissionUpdateAsync(new PropagationRolePermissionUpdateEvent { Entity = "permission", Action = "insert", ItemId = "p1" });
            _repo.Verify(r => r.GetPermissionByIdAsync("p1"), Times.Once);
        }

        // ---------- ProcessPermissionAsync ----------

        [Fact]
        public async Task ProcessPermission_Role_UpdatesCount_AndAudits()
        {
            _repo.Setup(r => r.UpdateRolesCountAsync("admin", It.IsAny<string>())).ReturnsAsync(true);
            var ok = await Create().ProcessPermissionAsync(new ResourceSetToPermissionMutationEvent
            {
                Entity = ResourceEntity.Role, Slug = "admin",
                AddPermissions = new List<string> { "p1" }, RemovePermissions = new List<string>(),
                IsPropagationEnable = false
            });
            ok.Should().BeTrue();
            _repo.Verify(r => r.UpdateRolesCountAsync("admin", It.IsAny<string>()), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Once);
        }

        // ---------- CreateOrganizationAsync ----------

        [Fact]
        public async Task CreateOrganization_MultiOrgDisabled_ReturnsError()
        {
            var result = await Create().CreateOrganizationAsync(new CreateOrganizationRequest { Name = "Org" });
            result.Errors.Should().ContainKey("multi_org_disabled");
        }

        [Fact]
        public async Task CreateOrganization_CloudDisabled_ReturnsError()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true, AllowOrgCreationFromCloud = false });
            var result = await Create().CreateOrganizationAsync(new CreateOrganizationRequest { Name = "Org", CreatedFrom = CreatedFrom.Cloud });
            result.Errors.Should().ContainKey("org_creation_disabled");
        }

        [Fact]
        public async Task CreateOrganization_NameExists_ReturnsError()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true, AllowOrgCreationFromCloud = true });
            _repo.Setup(r => r.GetOrganizationByNameAsync("Org")).ReturnsAsync(new Organization { Name = "Org" });
            var result = await Create().CreateOrganizationAsync(new CreateOrganizationRequest { Name = "Org", CreatedFrom = CreatedFrom.Cloud });
            result.Errors.Should().ContainKey("name_already_exists");
        }

        [Fact]
        public async Task CreateOrganization_Cloud_HappyPath_SavesAndProvisions()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true, AllowOrgCreationFromCloud = true });
            _repo.Setup(r => r.GetOrganizationByNameAsync("Org")).ReturnsAsync((Organization)null!);
            _repo.Setup(r => r.SaveOrganizationAsync(It.IsAny<Organization>())).Returns(Task.CompletedTask);

            var result = await Create().CreateOrganizationAsync(new CreateOrganizationRequest { Name = "Org", CreatedFrom = CreatedFrom.Cloud });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveOrganizationAsync(It.Is<Organization>(o => o.Name == "Org")), Times.Once);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<OrganizationProvisioningEvent>()), Times.Once);
        }

        [Fact]
        public async Task CreateOrganization_Portal_QueuesUserUpdate()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true, AllowOrgCreationFromPortal = true });
            _repo.Setup(r => r.GetOrganizationByNameAsync("Org")).ReturnsAsync((Organization)null!);
            _repo.Setup(r => r.SaveOrganizationAsync(It.IsAny<Organization>())).Returns(Task.CompletedTask);

            var result = await Create().CreateOrganizationAsync(new CreateOrganizationRequest { Name = "Org", CreatedFrom = CreatedFrom.ConstructPortal }, "creator-9");

            result.IsSuccess.Should().BeTrue();
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<UpdateOrganizationUserEvent>()), Times.Once);
        }

        // ---------- UpdateOrganizationAsync ----------

        [Fact]
        public async Task UpdateOrganization_MultiOrgDisabled_ReturnsError()
        {
            var result = await Create().UpdateOrganizationAsync("id1", new SaveOrganizationRequest { Name = "N" });
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateOrganization_EmptyId_ReturnsError()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            var result = await Create().UpdateOrganizationAsync("", new SaveOrganizationRequest { Name = "N" });
            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task UpdateOrganization_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.GetOrganizationById("id1")).ReturnsAsync((Organization)null!);
            var result = await Create().UpdateOrganizationAsync("id1", new SaveOrganizationRequest { Name = "N" });
            result.Errors.Should().ContainKey("not_found");
        }

        [Fact]
        public async Task UpdateOrganization_HappyPath_AppliesChanges()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            var org = new Organization { ItemId = "id1", Name = "Old" };
            _repo.Setup(r => r.GetOrganizationById("id1")).ReturnsAsync(org);
            _repo.Setup(r => r.SaveOrganizationAsync(It.IsAny<Organization>())).Returns(Task.CompletedTask);

            var result = await Create().UpdateOrganizationAsync("id1", new SaveOrganizationRequest { Name = "New", Email = "e@x.com" });

            result.IsSuccess.Should().BeTrue();
            org.Name.Should().Be("New");
            org.Email.Should().Be("e@x.com");
        }

        // ---------- GetOrganizations / GetOrganization / GetMyOrganization ----------

        [Fact]
        public async Task GetOrganizations_Disabled_ReturnsError()
        {
            var result = await Create().GetOrganizationsAsync(new GetOrganizationsRequest());
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("multi_org_disabled");
        }

        [Fact]
        public async Task GetOrganizations_HappyPath()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .ReturnsAsync(new GetOrganizationsResponse { IsSuccess = true, Organizations = new List<Organization>(), TotalCount = 0 });
            var result = await Create().GetOrganizationsAsync(new GetOrganizationsRequest());
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetOrganization_EmptyId_ReturnsError()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            var result = await Create().GetOrganizationAsync("");
            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task GetOrganization_HappyPath()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.GetOrganizationById("id1")).ReturnsAsync(new Organization { ItemId = "id1", Name = "N" });
            var result = await Create().GetOrganizationAsync("id1");
            result.IsSuccess.Should().BeTrue();
            result.Organization!.ItemId.Should().Be("id1");
        }

        [Fact]
        public async Task GetMyOrganization_Disabled_ReturnsError()
        {
            var result = await Create().GetMyOrganizationAsync();
            result.Errors.Should().ContainKey("multi_org_disabled");
        }

        [Fact]
        public async Task GetMyOrganization_NoOrgs_ReturnsEmptySuccess()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.GetOrganizationIdsByUserIdAsync("actor-1")).ReturnsAsync(new List<string>());
            var result = await Create().GetMyOrganizationAsync();
            result.IsSuccess.Should().BeTrue();
            result.Organizations.Should().BeEmpty();
        }

        [Fact]
        public async Task GetMyOrganization_HappyPath_MapsAndOrders()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.GetOrganizationIdsByUserIdAsync("actor-1")).ReturnsAsync(new List<string> { "o1", "o2" });
            _repo.Setup(r => r.GetOrganizationsByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new List<Organization> { new() { ItemId = "o2", Name = "Two" }, new() { ItemId = "o1", Name = "One" } });
            var result = await Create().GetMyOrganizationAsync();
            result.Organizations.Select(x => x.ItemId).Should().ContainInOrder("o1", "o2");
        }

        // ---------- SaveOrganizationConfig / GetOrganizationConfig ----------

        [Fact]
        public async Task SaveOrganizationConfig_NullExisting_CreatesNew()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((TenantConfiguration)null!);
            _repo.Setup(r => r.SaveOrganizationConfig(It.IsAny<TenantConfiguration>())).Returns(Task.CompletedTask);
            var result = await Create().SaveOrganizationConfigAsync(new SaveOrganizationConfigRequest { AllowOrgCreationFromCloud = true });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveOrganizationConfig(It.Is<TenantConfiguration>(c => c.AllowOrgCreationFromCloud)), Times.Once);
        }

        [Fact]
        public async Task SaveOrganizationConfig_ConsentEnablesMultiOrg()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { ConsentForMultiOrgEnable = false });
            _repo.Setup(r => r.SaveOrganizationConfig(It.IsAny<TenantConfiguration>())).Returns(Task.CompletedTask);
            await Create().SaveOrganizationConfigAsync(new SaveOrganizationConfigRequest { IsMultiOrgEnabled = true, ConsentForMultiOrgEnable = true });
            _repo.Verify(r => r.SaveOrganizationConfig(It.Is<TenantConfiguration>(c => c.IsMultiOrgEnabled && c.ConsentForMultiOrgEnable)), Times.Once);
        }

        [Fact]
        public async Task GetOrganizationConfig_NullConfig_ReturnsDefaults()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((TenantConfiguration)null!);
            var result = await Create().GetOrganizationConfigAsync();
            result["isMultiOrgEnabled"].Should().Be(false);
            result["itemId"].Should().Be("");
        }

        [Fact]
        public async Task GetOrganizationConfig_WithConfig_ReturnsValues()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { ItemId = "cfg-1", IsMultiOrgEnabled = true });
            var result = await Create().GetOrganizationConfigAsync();
            result["isMultiOrgEnabled"].Should().Be(true);
            result["itemId"].Should().Be("cfg-1");
        }

        // ---------- ExecuteOrganizationProvisioningAsync ----------

        [Fact]
        public async Task ExecuteOrganizationProvisioning_CopiesRolesAndPermissions()
        {
            _repo.Setup(r => r.GetRolesByOrgAsync("default")).ReturnsAsync(new List<Role> { new() { ItemId = "r1", Name = "R", Slug = "r" } });
            _repo.Setup(r => r.InsertRolesAsync(It.IsAny<List<Role>>())).ReturnsAsync(true);
            _repo.Setup(r => r.GetPermissionsByOrgAsync("default", It.IsAny<int?>(), It.IsAny<int?>())).ReturnsAsync(new List<Permission>());
            await Create().ExecuteOrganizationProvisioningAsync(new OrganizationProvisioningEvent { OrganizationId = "org-9", UserId = "u1" });
            _repo.Verify(r => r.InsertRolesAsync(It.Is<List<Role>>(l => l.All(x => x.CreatedFromDefault && x.OrganizationId == "org-9"))), Times.Once);
        }

        // ---------- Propagation: permission insert/update/delete for all orgs ----------

        [Fact]
        public async Task Propagation_PermissionInsert_ClonesToAllOrgs()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "P", Resource = "res" });
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .ReturnsAsync(new GetOrganizationsResponse { Organizations = new List<Organization> { new() { ItemId = "o1", Name = "O1" }, new() { ItemId = "o2", Name = "O2" } } });
            _repo.Setup(r => r.InsertPermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(new PropagationRolePermissionUpdateEvent { Entity = "permission", Action = "insert", ItemId = "p1" });

            _repo.Verify(r => r.InsertPermissionsAsync(It.Is<List<Permission>>(l => l.Count == 2 && l.Any(p => p.OrganizationId == "o1"))), Times.Once);
        }

        [Fact]
        public async Task Propagation_PermissionInsert_NoOrgs_Skips()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "P", Resource = "res" });
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .ReturnsAsync(new GetOrganizationsResponse { Organizations = new List<Organization>() });

            await Create().ExecutePropagationRolePermissionUpdateAsync(new PropagationRolePermissionUpdateEvent { Entity = "permission", Action = "insert", ItemId = "p1" });

            _repo.Verify(r => r.InsertPermissionsAsync(It.IsAny<List<Permission>>()), Times.Never);
        }

        [Fact]
        public async Task Propagation_PermissionUpdate_UpdatesNonDefaultOrgs()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "New", Resource = "res" });
            _repo.Setup(r => r.GetPermissionsByResourceAsync("res")).ReturnsAsync(new List<Permission>
            {
                new() { ItemId = "px", Name = "Old", Resource = "res", OrganizationId = "o1" },
                new() { ItemId = "pd", Name = "Old", Resource = "res", OrganizationId = "default" }
            });
            _repo.Setup(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(new PropagationRolePermissionUpdateEvent { Entity = "permission", Action = "update", ItemId = "p1" });

            _repo.Verify(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>()), Times.Once);
        }

        [Fact]
        public async Task Propagation_PermissionDelete_ArchivesNonDefaultOrgs()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "P", Resource = "res" });
            _repo.Setup(r => r.GetPermissionsByResourceAsync("res")).ReturnsAsync(new List<Permission>
            {
                new() { ItemId = "px", Name = "P", Resource = "res", OrganizationId = "o1", IsArchived = false }
            });
            _repo.Setup(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(new PropagationRolePermissionUpdateEvent { Entity = "permission", Action = "delete", ItemId = "p1" });

            _repo.Verify(r => r.UpdatePermissionsAsync(It.Is<List<Permission>>(l => l.Any(p => p.IsArchived))), Times.Once);
        }

        [Fact]
        public async Task Propagation_RoleInsert_ClonesMissingOrgs()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Name = "R", Slug = "admin" });
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .ReturnsAsync(new GetOrganizationsResponse { Organizations = new List<Organization> { new() { ItemId = "o1", Name = "O1" } } });
            _repo.Setup(r => r.GetRoleBySlugAsync("admin", "o1")).ReturnsAsync((Role)null!);
            _repo.Setup(r => r.InsertRolesAsync(It.IsAny<List<Role>>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(new PropagationRolePermissionUpdateEvent { Entity = "role", Action = "insert", ItemId = "r1" });

            _repo.Verify(r => r.InsertRolesAsync(It.Is<List<Role>>(l => l.Any(x => x.OrganizationId == "o1" && x.CreatedFromDefault))), Times.Once);
        }

        [Fact]
        public async Task Propagation_RoleUpdate_UpdatesDefaultDerivedRoles()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Name = "New", Slug = "admin" });
            _repo.Setup(r => r.GetRolesBySlugAsync("admin")).ReturnsAsync(new List<Role>
            {
                new() { ItemId = "rx", Name = "Old", Slug = "admin", CreatedFromDefault = true, OrganizationId = "o1" }
            });
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(new PropagationRolePermissionUpdateEvent { Entity = "role", Action = "update", ItemId = "r1" });

            _repo.Verify(r => r.UpdateRolesAsync(It.Is<List<Role>>(l => l.Any(x => x.Name == "New"))), Times.Once);
        }

        [Fact]
        public async Task Propagation_RoleDelete_LogsButNoRepoDelete()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Name = "R", Slug = "admin" });
            _repo.Setup(r => r.GetRolesBySlugAsync("admin")).ReturnsAsync(new List<Role>
            {
                new() { ItemId = "rx", Name = "R", Slug = "admin", CreatedFromDefault = true, OrganizationId = "o1" }
            });

            await Create().ExecutePropagationRolePermissionUpdateAsync(new PropagationRolePermissionUpdateEvent { Entity = "role", Action = "delete", ItemId = "r1" });
            // No exception; role delete path only logs.
        }

        // ---------- ExecuteResourceMutationCommandAsync ----------

        [Fact]
        public async Task ExecuteResourceMutationCommand_Null_NoThrow()
        {
            await Create().ExecuteResourceMutationCommandAsync(null!);
        }

        [Fact]
        public async Task ExecuteResourceMutationCommand_Permission_AuditsAndPropagatesBuiltIn()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "P", Resource = "res", IsBuiltIn = true });
            await Create().ExecuteResourceMutationCommandAsync(new ResourceMutationEvent { Entity = ResourceEntity.Permission, Action = MutationEventType.Create, ItemId = "p1" });
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PermissionMutationForTenantsEvent>()), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Event == "PERMISSION_CREATED")), Times.Once);
        }

        [Fact]
        public async Task ExecuteResourceMutationCommand_Role_UpdatesCount()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("r1")).ReturnsAsync(new Role { ItemId = "r1", Name = "R", Slug = "admin" });
            _repo.Setup(r => r.UpdateRolesCountAsync("admin", It.IsAny<string>())).ReturnsAsync(true);
            await Create().ExecuteResourceMutationCommandAsync(new ResourceMutationEvent { Entity = ResourceEntity.Role, Action = MutationEventType.Update, ItemId = "r1" });
            _repo.Verify(r => r.UpdateRolesCountAsync("admin", It.IsAny<string>()), Times.Once);
        }

        // ---------- ExecutePermissionMutationForTenantsAsync ----------

        [Fact]
        public async Task ExecutePermissionMutationForTenants_PropagatesAndAudits()
        {
            _propagator.Setup(p => p.PropagateAsync(It.IsAny<PermissionMutationForTenantsEvent>()))
                .ReturnsAsync(new PropagationSummary { TenantsAttempted = 2, TenantsSucceeded = 2, TenantsFailed = 0 });

            await Create().ExecutePermissionMutationForTenantsAsync(new PermissionMutationForTenantsEvent { ItemId = "p1", Action = MutationEventType.Create });

            _propagator.Verify(p => p.PropagateAsync(It.IsAny<PermissionMutationForTenantsEvent>()), Times.Once);
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Event == "PERMISSION_PROPAGATED" && e.Outcome == "success")), Times.Once);
        }

        [Fact]
        public async Task ExecutePermissionMutationForTenants_PartialFailure_Outcome()
        {
            _propagator.Setup(p => p.PropagateAsync(It.IsAny<PermissionMutationForTenantsEvent>()))
                .ReturnsAsync(new PropagationSummary { TenantsAttempted = 2, TenantsSucceeded = 1, TenantsFailed = 1 });

            await Create().ExecutePermissionMutationForTenantsAsync(new PermissionMutationForTenantsEvent { ItemId = "p1", Action = MutationEventType.Update });

            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Outcome == "partial_failure")), Times.Once);
        }
    }
}
