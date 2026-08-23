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
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
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
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>())).ReturnsAsync(true);
            _repo.Setup(r => r.HasChildRolesAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            _repo.Setup(r => r.HasUserAssignmentsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            _repo.Setup(r => r.RemoveRoleFromAllPermissionsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRoleFromAllUsersAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemovePermissionFromAllUsersAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRoleFromSignUpDefaultsAsync(It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemovePermissionFromSignUpDefaultsAsync(It.IsAny<string>())).ReturnsAsync(true);
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

        private ResourceMutationService Create(ILogger<ResourceMutationService> logger) =>
            new(logger, _repo.Object, _iam.Object,
                _permValidator.Object, _updatePermValidator.Object, _roleValidator.Object,
                _propagator.Object, _activity.Object);

        /// <summary>
        /// Captures warning-level messages, so tests can assert that a failure the caller cannot
        /// see was at least reported. Used where the only observable outcome is a log line.
        /// </summary>
        private static Mock<ILogger<ResourceMutationService>> WarningCapture(List<string> sink)
        {
            var logger = new Mock<ILogger<ResourceMutationService>>();
            logger.Setup(l => l.Log(
                    It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(invocation =>
                {
                    if ((LogLevel)invocation.Arguments[0] != LogLevel.Warning) return;
                    sink.Add(invocation.Arguments[2]?.ToString() ?? string.Empty);
                }));
            return logger;
        }

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
        public async Task UpdatePermission_ArchiveRequested_IsRefusedAndWritesNothing()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "old", Resource = "old" });

            var result = await Create().UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "n", Resource = "r", ResourceGroup = "g", IsArchived = true });

            // Archiving belongs to the delete endpoint alone. Refused rather than ignored so a
            // caller aiming here is told, instead of getting a 200 for a delete that never ran.
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("IsArchived");
            _repo.Verify(r => r.UpdatePermissionAsync(It.IsAny<Permission>()), Times.Never);
        }

        [Fact]
        public async Task UpdatePermission_ArchivedPermission_CannotBeEdited()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1"))
                .ReturnsAsync(new Permission { ItemId = "p1", Name = "old", Resource = "old", IsArchived = true });

            var result = await Create().UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "n", Resource = "r", ResourceGroup = "g" });

            // Editing one used to resurrect it: UpdateAllSamePermissionAsync writes IsArchived to
            // every organization's copy, so a name change revived it tenant-wide with its role
            // bindings intact.
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("archived");
            _repo.Verify(r => r.UpdatePermissionAsync(It.IsAny<Permission>()), Times.Never);
        }

        [Fact]
        public async Task UpdatePermission_LeavesLifecycleFlagsAlone()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1"))
                .ReturnsAsync(new Permission { ItemId = "p1", Name = "old", Resource = "old", IsBuiltIn = true });

            // Both flags are non-nullable bools on the request, so this is also what a client that
            // omits them sends. Applying them used to strip IsBuiltIn on an unrelated edit, which
            // defeated the root-tenant guard in ArchivePermissionAsync.
            var result = await Create().UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "n", Resource = "r", ResourceGroup = "g", IsBuiltIn = false });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdatePermissionAsync(It.Is<Permission>(p => p.IsBuiltIn && !p.IsArchived)), Times.Once);
        }

        [Fact]
        public async Task UpdatePermission_PropagatesAsUpdateNeverAsDelete()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("p1")).ReturnsAsync(new Permission { ItemId = "p1", Name = "old", Resource = "old" });
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            var result = await Create().UpdatePermissionAsync("p1", new UpdatePermissionRequest { Name = "n", Resource = "r", ResourceGroup = "g" });

            result.IsSuccess.Should().BeTrue();
            // The "delete" action routed into DeletePermissionForAllOrg, which skips the default
            // organization on the assumption the archive already handled it -- something this
            // method never did, leaving the default organization's role counts stale.
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.Is<PropagationRolePermissionUpdateEvent>(e => e.Action == "update")), Times.Once);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.Is<PropagationRolePermissionUpdateEvent>(e => e.Action == "delete")), Times.Never);
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

        // ---------- ArchivePermissionAsync ----------

        private static readonly DateTime StaleTimestamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        /// <summary>
        /// Every field carries a distinguishable, non-default value. An accidental assignment that
        /// reset a field to its default -- clearing DependentPermissions, say -- would be invisible
        /// against an empty fixture, and UpdateAllSamePermissionAsync would then push that reset
        /// onto every other organization's copy of the permission.
        /// </summary>
        private static Permission ArchiveTarget(bool isBuiltIn = false, bool isArchived = false, string orgId = "default") => new()
        {
            ItemId = "p1",
            Name = "Reports Export",
            Description = "Export reports",
            Resource = "reports::export",
            ResourceGroup = "reports",
            Type = ResourceType.Endpoint,
            PermissionSeverity = PermissionSeverity.High,
            Tags = new List<string> { "reporting" },
            DependentPermissions = new List<string> { "reports::read" },
            Roles = new List<string> { "admin" },
            OrganizationId = orgId,
            IsBuiltIn = isBuiltIn,
            IsArchived = isArchived,
            CreatedBy = "creator-1",
            CreatedDate = StaleTimestamp,
            LastUpdatedDate = StaleTimestamp,
            LastUpdatedBy = "someone-else"
        };

        private void GivenPermission(Permission permission) =>
            _repo.Setup(r => r.GetPermissionByIdAsync(permission.ItemId)).ReturnsAsync(permission);

        /// <summary>
        /// Every rejection path must be silent as well as wrong-free: an archive endpoint that
        /// returns the right error while still writing or emitting a delete event would satisfy a
        /// looser assertion and still be broken.
        /// </summary>
        private void VerifyNothingHappened()
        {
            _repo.Verify(r => r.UpdatePermissionAsync(It.IsAny<Permission>()), Times.Never);
            _repo.Verify(r => r.UpdateAllSamePermissionAsync(It.IsAny<Permission>()), Times.Never);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<ResourceMutationEvent>()), Times.Never);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Never);
            // Nothing was archived, so no role's count has moved. Rewriting one here would leave a
            // number that disagrees with a permission still very much in force.
            _repo.Verify(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ArchivePermission_CustomPermission_ArchivesAndReturnsItemId()
        {
            var permission = ArchiveTarget();
            GivenPermission(permission);

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("p1");
            _repo.Verify(r => r.UpdatePermissionAsync(It.Is<Permission>(p => p.ItemId == "p1" && p.IsArchived && p.LastUpdatedBy == "actor-1")), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_TouchesOnlyTheArchiveFlagAndAuditStamp()
        {
            Permission? persisted = null;
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.UpdatePermissionAsync(It.IsAny<Permission>()))
                .Callback<Permission>(p => persisted = p)
                .ReturnsAsync(true);

            await Create().ArchivePermissionAsync("p1");

            persisted.Should().NotBeNull();
            persisted!.IsArchived.Should().BeTrue();
            persisted.LastUpdatedBy.Should().Be("actor-1");
            persisted.LastUpdatedDate.Should().BeAfter(StaleTimestamp);

            // This is an archive, not an update: nothing descriptive may be rewritten. The record
            // is also handed to UpdateAllSamePermissionAsync, whose $set pushes these very fields
            // onto every other organization's copy, so a stray assignment here would leak.
            var pristine = ArchiveTarget();
            persisted.Should().BeEquivalentTo(pristine, options => options
                .Excluding(p => p.IsArchived)
                .Excluding(p => p.LastUpdatedBy)
                .Excluding(p => p.LastUpdatedDate));
        }

        [Fact]
        public async Task ArchivePermission_BuiltInAsRoot_Succeeds()
        {
            GivenPermission(ArchiveTarget(isBuiltIn: true));
            _iam.Setup(i => i.IsRoot()).Returns(true);

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdatePermissionAsync(It.Is<Permission>(p => p.IsArchived)), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_Success_SendsDeleteMutationEventToResourceQueue()
        {
            GivenPermission(ArchiveTarget());

            await Create().ArchivePermissionAsync("p1");

            _iam.Verify(i => i.SendToQueueAsync(
                IdpConstants.IamResourceQueue,
                It.Is<ResourceMutationEvent>(e =>
                    e.Action == MutationEventType.Delete &&
                    e.Entity == ResourceEntity.Permission &&
                    e.ItemId == "p1")), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_Success_PropagatesToOtherOrgsSynchronously()
        {
            GivenPermission(ArchiveTarget());

            await Create().ArchivePermissionAsync("p1");

            _repo.Verify(r => r.UpdateAllSamePermissionAsync(It.Is<Permission>(p => p.Resource == "reports::export" && p.IsArchived)), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_MultiOrgEnabled_QueuesPropagationEvent()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            await Create().ArchivePermissionAsync("p1");

            _iam.Verify(i => i.SendToQueueAsync(
                IdpConstants.IamOrgQueue,
                It.Is<PropagationRolePermissionUpdateEvent>(e =>
                    e.Entity == "permission" && e.Action == "delete" && e.ItemId == "p1")), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_MultiOrgDisabled_StillWritesCrossOrgButQueuesNoPropagationEvent()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = false });

            await Create().ArchivePermissionAsync("p1");

            // The asymmetry is deliberate and pre-existing (spec A3): the synchronous cross-org
            // write is unconditional, only the queued event is gated on multi-org.
            _repo.Verify(r => r.UpdateAllSamePermissionAsync(It.IsAny<Permission>()), Times.Once);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Never);
        }

        [Fact]
        public async Task ArchivePermission_NoTenantConfiguration_StillSucceeds()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((TenantConfiguration)null!);

            var result = await Create().ArchivePermissionAsync("p1");

            // The config is read after the archive has committed, so a null must not turn a
            // successful archive into a 500 that the client cannot safely retry.
            result.IsSuccess.Should().BeTrue();
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Never);
        }

        [Fact]
        public async Task ArchivePermission_CustomPermissionNonRootCaller_StillSucceeds()
        {
            GivenPermission(ArchiveTarget());
            _iam.Setup(i => i.IsRoot()).Returns(false);

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task ArchivePermission_NonDefaultOrgCaller_ForbiddenAndTouchesNothing()
        {
            InstallContext(orgId: "acme");
            GivenPermission(ArchiveTarget());

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeFalse();
            result.Errors["forbidden"].Should().Be("Not_Allowed_To_Archive_Permission_Outside_Default_Organization");
            _repo.Verify(r => r.GetPermissionByIdAsync(It.IsAny<string>()), Times.Never);
            _iam.Verify(i => i.IsRoot(), Times.Never);
            VerifyNothingHappened();
        }

        [Fact]
        public async Task ArchivePermission_NotFound_ReturnsNotFoundAndTouchesNothing()
        {
            _repo.Setup(r => r.GetPermissionByIdAsync("does-not-exist")).ReturnsAsync((Permission)null!);

            var result = await Create().ArchivePermissionAsync("does-not-exist");

            result.IsSuccess.Should().BeFalse();
            result.Errors["ItemId"].Should().Be("Permission_Not_Found");
            VerifyNothingHappened();
        }

        [Fact]
        public async Task ArchivePermission_RecordBelongsToAnotherOrg_ForbiddenAndTouchesNothing()
        {
            GivenPermission(ArchiveTarget(orgId: "acme"));

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeFalse();
            result.Errors["forbidden"].Should().Be("Permission_Not_A_Default_Organization_Record");
            _iam.Verify(i => i.IsRoot(), Times.Never);
            VerifyNothingHappened();
        }

        [Fact]
        public async Task ArchivePermission_AlreadyArchived_ReturnsErrorAndTouchesNothing()
        {
            GivenPermission(ArchiveTarget(isArchived: true));

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeFalse();
            result.Errors["archived"].Should().Be("Permission_Already_Archived");
            _iam.Verify(i => i.IsRoot(), Times.Never);
            VerifyNothingHappened();
        }

        [Fact]
        public async Task ArchivePermission_BuiltInNonRoot_ForbiddenAndTouchesNothing()
        {
            GivenPermission(ArchiveTarget(isBuiltIn: true));
            _iam.Setup(i => i.IsRoot()).Returns(false);

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeFalse();
            result.Errors["forbidden"].Should().Be("Only_Root_Tenant_Can_Archive_Built_In_Permission");
            VerifyNothingHappened();
        }

        [Fact]
        public async Task ArchivePermission_RepositoryWriteFails_ReturnsGenericFailureAndSendsNoEvents()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.UpdatePermissionAsync(It.IsAny<Permission>())).ReturnsAsync(false);
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeFalse();
            _repo.Verify(r => r.UpdateAllSamePermissionAsync(It.IsAny<Permission>()), Times.Never);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<ResourceMutationEvent>()), Times.Never);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Never);
        }

        // ---------- Guard precedence (spec A4) ----------
        // Each of these rows fails more than one guard. They pin which error wins, so a plausible
        // reordering -- checking built-in first as the "most important" rule, say -- fails loudly
        // instead of silently changing the contract the ticket's examples specify.

        [Fact]
        public async Task ArchivePermission_NonDefaultCallerTargetingBuiltIn_ReportsCallerOrgFirst()
        {
            InstallContext(orgId: "acme");
            GivenPermission(ArchiveTarget(isBuiltIn: true));
            _iam.Setup(i => i.IsRoot()).Returns(false);

            var result = await Create().ArchivePermissionAsync("p1");

            result.Errors["forbidden"].Should().Be("Not_Allowed_To_Archive_Permission_Outside_Default_Organization");
        }

        [Fact]
        public async Task ArchivePermission_NonDefaultCallerTargetingMissingId_ReportsCallerOrgNotNotFound()
        {
            InstallContext(orgId: "acme");
            _repo.Setup(r => r.GetPermissionByIdAsync("nope")).ReturnsAsync((Permission)null!);

            var result = await Create().ArchivePermissionAsync("nope");

            result.Errors.Should().NotContainKey("ItemId");
            result.Errors["forbidden"].Should().Be("Not_Allowed_To_Archive_Permission_Outside_Default_Organization");
        }

        [Fact]
        public async Task ArchivePermission_ForeignOrgRecordAlreadyArchived_ReportsOrgMismatchFirst()
        {
            GivenPermission(ArchiveTarget(isArchived: true, orgId: "acme"));

            var result = await Create().ArchivePermissionAsync("p1");

            result.Errors.Should().NotContainKey("archived");
            result.Errors["forbidden"].Should().Be("Permission_Not_A_Default_Organization_Record");
        }

        [Fact]
        public async Task ArchivePermission_ForeignOrgBuiltInRecordNonRoot_ReportsOrgMismatchFirst()
        {
            GivenPermission(ArchiveTarget(isBuiltIn: true, orgId: "acme"));
            _iam.Setup(i => i.IsRoot()).Returns(false);

            var result = await Create().ArchivePermissionAsync("p1");

            result.Errors["forbidden"].Should().Be("Permission_Not_A_Default_Organization_Record");
            // Asserting the winning error alone would still pass if the built-in check ran first
            // and merely lost the race to report. The earlier guard must short-circuit.
            _iam.Verify(i => i.IsRoot(), Times.Never);
        }

        [Fact]
        public async Task ArchivePermission_ArchivedBuiltInNonRoot_ReportsAlreadyArchivedFirst()
        {
            GivenPermission(ArchiveTarget(isBuiltIn: true, isArchived: true));
            _iam.Setup(i => i.IsRoot()).Returns(false);

            var result = await Create().ArchivePermissionAsync("p1");

            result.Errors.Should().NotContainKey("forbidden");
            result.Errors["archived"].Should().Be("Permission_Already_Archived");
            _iam.Verify(i => i.IsRoot(), Times.Never);
        }

        // ---------- ArchiveRoleAsync (#456) ----------

        private static Role ArchiveRoleTarget(
            string org = "default", bool createdFromDefault = false, bool isArchived = false) => new()
            {
                ItemId = "r1",
                Slug = "manager",
                Name = "Manager",
                Description = "d",
                OrganizationId = org,
                CreatedFromDefault = createdFromDefault,
                IsArchived = isArchived,
                LastUpdatedDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdatedBy = "someone-else"
            };

        private void GivenRole(Role role) =>
            _repo.Setup(r => r.GetRoleByIdAsync(role.ItemId)).ReturnsAsync(role);

        private void VerifyRoleUntouched()
        {
            _repo.Verify(r => r.UpdateRoleAsync(It.IsAny<Role>()), Times.Never);
            _repo.Verify(r => r.RemoveRoleFromAllPermissionsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.RemoveRoleFromAllUsersAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<ResourceMutationEvent>()), Times.Never);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Never);
        }

        [Fact]
        public async Task ArchiveRole_OrgOwnedRole_ArchivesAndStampsAudit()
        {
            InstallContext(orgId: "acme");
            Role? persisted = null;
            GivenRole(ArchiveRoleTarget(org: "acme"));
            _repo.Setup(r => r.UpdateRoleAsync(It.IsAny<Role>()))
                .Callback<Role>(r => persisted = r)
                .ReturnsAsync(true);

            var result = await Create().ArchiveRoleAsync("r1");

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("r1");
            persisted!.IsArchived.Should().BeTrue();
            // The copies inherit the source's LastUpdatedBy, so a stale stamp here would attribute
            // the archive to whoever last edited the role, in every organization.
            persisted.LastUpdatedBy.Should().Be("actor-1");
            persisted.LastUpdatedDate.Should().BeAfter(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            persisted.LastUpdatedDate.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Fact]
        public async Task ArchiveRole_CleansPermissionsAndUsersBeforePersisting()
        {
            InstallContext(orgId: "acme");
            var order = new List<string>();
            GivenRole(ArchiveRoleTarget(org: "acme"));
            _repo.Setup(r => r.RemoveRoleFromAllPermissionsAsync("manager", "acme"))
                .Callback(() => order.Add("permissions")).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRoleFromAllUsersAsync("manager", "acme"))
                .Callback(() => order.Add("users")).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRoleAsync(It.IsAny<Role>()))
                .Callback(() => order.Add("persist")).ReturnsAsync(true);

            await Create().ArchiveRoleAsync("r1");

            // Verifying that all three happened says nothing about order, and the ordering is the
            // whole point of A2 -- so assert the sequence itself.
            order.Should().Equal("permissions", "users", "persist");
        }

        [Fact]
        public async Task ArchiveRole_RemovesTheSlugFromTheSignUpDefaults()
        {
            InstallContext(orgId: "acme");
            GivenRole(ArchiveRoleTarget(org: "acme"));

            var result = await Create().ArchiveRoleAsync("r1");

            result.IsSuccess.Should().BeTrue();
            // DefaultRolesForNewUserOnSignUp is copied verbatim onto every account created after
            // this point, with no archived check on that path -- so a slug left here keeps being
            // written into User.Roles for new signups. Tenant-wide, hence no organization argument.
            _repo.Verify(r => r.RemoveRoleFromSignUpDefaultsAsync("manager"), Times.Once);
        }

        [Fact]
        public async Task ArchiveRole_SignUpDefaultsCleanupUnacknowledged_LeavesRoleActive()
        {
            InstallContext(orgId: "acme");
            GivenRole(ArchiveRoleTarget(org: "acme"));
            _repo.Setup(r => r.RemoveRoleFromSignUpDefaultsAsync(It.IsAny<string>())).ReturnsAsync(false);

            var result = await Create().ArchiveRoleAsync("r1");

            // Same reasoning as the other reference cleanups: an archived role still being handed to
            // every new signup is the worse half-state, so the archive waits for a retry.
            result.IsSuccess.Should().BeFalse();
            _repo.Verify(r => r.UpdateRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task ArchiveRole_DefaultOrgMasterRole_Succeeds()
        {
            GivenRole(ArchiveRoleTarget());

            var result = await Create().ArchiveRoleAsync("r1");

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task ArchiveRole_Success_SendsDeleteMutationEvent()
        {
            GivenRole(ArchiveRoleTarget());

            await Create().ArchiveRoleAsync("r1");

            _iam.Verify(i => i.SendToQueueAsync(
                IdpConstants.IamResourceQueue,
                It.Is<ResourceMutationEvent>(e =>
                    e.Action == MutationEventType.Delete &&
                    e.Entity == ResourceEntity.Role &&
                    e.ItemId == "r1")), Times.Once);
        }

        [Fact]
        public async Task ArchiveRole_MultiOrgEnabled_QueuesRolePropagationEvent()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            await Create().ArchiveRoleAsync("r1");

            _iam.Verify(i => i.SendToQueueAsync(
                IdpConstants.IamOrgQueue,
                It.Is<PropagationRolePermissionUpdateEvent>(e =>
                    e.Entity == "role" && e.Action == "delete" && e.ItemId == "r1")), Times.Once);
        }

        [Fact]
        public async Task ArchiveRole_MultiOrgDisabled_QueuesNoPropagationEvent()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = false });

            await Create().ArchiveRoleAsync("r1");

            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Never);
        }

        /// <summary>
        /// The guards resolve a blank organization to "default", so a caller in that state can
        /// archive the master record. The propagation gate must resolve it the same way, or the
        /// master is archived while every copy in every other organization stays active. Every
        /// other test here installs a literal "default", so this is the only one that would notice.
        /// </summary>
        [Fact]
        public async Task ArchiveRole_BlankContextOrganization_StillQueuesPropagation()
        {
            InstallContext(orgId: "");
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            var result = await Create().ArchiveRoleAsync("r1");

            result.IsSuccess.Should().BeTrue();
            _iam.Verify(i => i.SendToQueueAsync(
                IdpConstants.IamOrgQueue,
                It.Is<PropagationRolePermissionUpdateEvent>(e => e.Entity == "role" && e.Action == "delete")), Times.Once);
        }

        [Fact]
        public async Task ArchiveRole_NoTenantConfiguration_StillSucceeds()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync((TenantConfiguration)null!);

            var result = await Create().ArchiveRoleAsync("r1");

            result.IsSuccess.Should().BeTrue();
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Never);
        }

        /// <summary>
        /// The events are published after the archive has committed, and a retry cannot republish
        /// them because it stops at Role_Already_Archived. Letting the exception escape would
        /// return 500 for a write that succeeded and still leave the same gap, so the failure is
        /// logged instead — loudly enough to drive the reconciliation pass, since for roles this
        /// queue message is the only cross-organization channel there is.
        /// </summary>
        [Fact]
        public async Task ArchiveRole_EventPublishingThrows_StillReportsSuccessAndLogsTheGap()
        {
            var warnings = new List<string>();
            var logger = new Mock<ILogger<ResourceMutationService>>();
            var errors = new List<string>();
            logger.Setup(l => l.Log(
                    It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(inv =>
                {
                    if ((LogLevel)inv.Arguments[0] == LogLevel.Error) errors.Add(inv.Arguments[2]?.ToString() ?? "");
                }));

            GivenRole(ArchiveRoleTarget());
            _iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<ResourceMutationEvent>()))
                .ThrowsAsync(new InvalidOperationException("queue down"));

            var result = await Create(logger.Object).ArchiveRoleAsync("r1");

            result.IsSuccess.Should().BeTrue("the archive itself committed");
            _repo.Verify(r => r.UpdateRoleAsync(It.Is<Role>(x => x.IsArchived)), Times.Once);
            errors.Should().ContainSingle(e => e.Contains("manager") && e.Contains("reconciliation"));
            warnings.Should().BeEmpty();
        }

        [Fact]
        public async Task ArchiveRole_NotFound_ReturnsErrorAndTouchesNothing()
        {
            _repo.Setup(r => r.GetRoleByIdAsync("nope")).ReturnsAsync((Role)null!);

            var result = await Create().ArchiveRoleAsync("nope");

            result.Errors["ItemId"].Should().Be("Role_Not_Found");
            VerifyRoleUntouched();
        }

        [Fact]
        public async Task ArchiveRole_DefaultCopiedRole_ForbiddenAndTouchesNothing()
        {
            InstallContext(orgId: "acme");
            GivenRole(ArchiveRoleTarget(org: "acme", createdFromDefault: true));

            var result = await Create().ArchiveRoleAsync("r1");

            result.Errors["forbidden"].Should().Be("Can_Not_Archive_Default_Copied_Role");
            VerifyRoleUntouched();
        }

        [Fact]
        public async Task ArchiveRole_RoleFromAnotherOrganization_ForbiddenAndTouchesNothing()
        {
            InstallContext(orgId: "acme");
            GivenRole(ArchiveRoleTarget(org: "globex"));

            var result = await Create().ArchiveRoleAsync("r1");

            result.Errors["forbidden"].Should().Be("Not_Allowed_To_Archive_Role_From_Another_Organization");
            VerifyRoleUntouched();
        }

        /// <summary>
        /// The organization comparison applies to default-org callers too. GetRoleByIdAsync has no
        /// organization scope and the copied-role guard only catches CreatedFromDefault records, so
        /// restricting this check to non-default callers would let a default-org admin archive
        /// another organization's own role by id.
        /// </summary>
        [Fact]
        public async Task ArchiveRole_DefaultOrgCallerTargetingAnotherOrgsOwnRole_IsStillForbidden()
        {
            GivenRole(ArchiveRoleTarget(org: "acme", createdFromDefault: false));

            var result = await Create().ArchiveRoleAsync("r1");

            result.Errors["forbidden"].Should().Be("Not_Allowed_To_Archive_Role_From_Another_Organization");
            VerifyRoleUntouched();
        }

        [Fact]
        public async Task ArchiveRole_AlreadyArchived_ReturnsErrorAndTouchesNothing()
        {
            GivenRole(ArchiveRoleTarget(isArchived: true));

            var result = await Create().ArchiveRoleAsync("r1");

            result.Errors["archived"].Should().Be("Role_Already_Archived");
            VerifyRoleUntouched();
            _repo.Verify(r => r.HasChildRolesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ArchiveRole_HasChildRoles_BlockedAndTouchesNothing()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.HasChildRolesAsync("manager", "default")).ReturnsAsync(true);

            var result = await Create().ArchiveRoleAsync("r1");

            result.Errors["dependency"].Should().Be("Role_Has_Child_Roles");
            VerifyRoleUntouched();
        }

        [Fact]
        public async Task ArchiveRole_HasActiveUserAssignments_BlockedAndTouchesNothing()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.HasUserAssignmentsAsync("manager", "default")).ReturnsAsync(true);

            var result = await Create().ArchiveRoleAsync("r1");

            result.Errors["dependency"].Should().Be("Role_Has_Active_User_Assignments");
            VerifyRoleUntouched();
        }

        // ---------- Consented archive (#465) ----------

        [Fact]
        public async Task ArchiveRole_WithConsent_ArchivesDespiteActiveHoldersAndScrubsThem()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.HasUserAssignmentsAsync("manager", "default")).ReturnsAsync(true);

            var result = await Create().ArchiveRoleAsync("r1", confirmRevokeFromUsers: true);

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdateRoleAsync(It.Is<Role>(x => x.IsArchived)), Times.Once);
            _repo.Verify(r => r.RemoveRoleFromAllUsersAsync("manager", "default"), Times.Once);
        }

        [Fact]
        public async Task ArchiveRole_WithConsent_WarnsThatLiveAccessIsBeingRevoked()
        {
            var warnings = new List<string>();
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.HasUserAssignmentsAsync("manager", "default")).ReturnsAsync(true);

            await Create(WarningCapture(warnings).Object).ArchiveRoleAsync("r1", confirmRevokeFromUsers: true);

            // Consent is exercised silently as far as the caller is concerned; this log is the only
            // durable record that live access was revoked.
            warnings.Should().ContainSingle(w => w.Contains("manager") && w.Contains("default"));
        }

        [Fact]
        public async Task ArchiveRole_WithConsent_StillRefusesChildRoles()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.HasChildRolesAsync("manager", "default")).ReturnsAsync(true);

            var result = await Create().ArchiveRoleAsync("r1", confirmRevokeFromUsers: true);

            // Consent covers revocation, never structural corruption: a descendant left pointing at
            // an archived parent has no repair path, because re-parenting is not implemented.
            result.Errors["dependency"].Should().Be("Role_Has_Child_Roles");
            VerifyRoleUntouched();
        }

        [Fact]
        public async Task ArchiveRole_WithConsent_StillRefusesAnotherOrganizationsRole()
        {
            InstallContext(orgId: "acme");
            GivenRole(ArchiveRoleTarget(org: "globex"));

            var result = await Create().ArchiveRoleAsync("r1", confirmRevokeFromUsers: true);

            // The consent gate sits BELOW authorization; it must never widen who may archive what.
            result.Errors["forbidden"].Should().Be("Not_Allowed_To_Archive_Role_From_Another_Organization");
            VerifyRoleUntouched();
        }

        [Fact]
        public async Task ArchiveRole_WithConsent_AlreadyArchived_DoesNotScrubAnyone()
        {
            GivenRole(ArchiveRoleTarget(isArchived: true));

            var result = await Create().ArchiveRoleAsync("r1", confirmRevokeFromUsers: true);

            // A repeated consented call must not re-run a destructive scrub.
            result.Errors["archived"].Should().Be("Role_Already_Archived");
            VerifyRoleUntouched();
        }

        [Fact]
        public async Task ArchiveRole_WithConsent_CarriesConsentOnThePropagationEvent()
        {
            InstallContext(orgId: "default");
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            await Create().ArchiveRoleAsync("r1", confirmRevokeFromUsers: true);

            // Propagation runs later and cannot re-derive whether a human agreed, so the consent
            // has to travel on the message.
            _iam.Verify(i => i.SendToQueueAsync(
                IdpConstants.IamOrgQueue,
                It.Is<PropagationRolePermissionUpdateEvent>(e =>
                    e.Entity == "role" && e.Action == "delete" && e.ConfirmRevokeFromUsers)), Times.Once);
        }

        [Fact]
        public async Task ArchiveRole_WithoutConsent_PropagationEventCarriesFalse()
        {
            InstallContext(orgId: "default");
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });

            await Create().ArchiveRoleAsync("r1");

            _iam.Verify(i => i.SendToQueueAsync(
                IdpConstants.IamOrgQueue,
                It.Is<PropagationRolePermissionUpdateEvent>(e => !e.ConfirmRevokeFromUsers)), Times.Once);
        }

        [Fact]
        public async Task PropagateRoleDelete_WithConsent_ArchivesCopyWithActiveAssignments()
        {
            List<Role>? written = null;
            GivenSourceAndCopies(Copy("c-acme", "acme"), Copy("c-globex", "globex"));
            _repo.Setup(r => r.HasUserAssignmentsAsync("manager", "globex")).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()))
                .Callback<List<Role>>(x => written = x).ReturnsAsync(true);

            await PropagateRoleDelete(confirmRevokeFromUsers: true);

            // Leaving globex active would be the split-brain state this propagation exists to
            // prevent: gone from the admin's list, still working there, and unarchivable directly.
            written!.Select(x => x.ItemId).Should().Equal("c-acme", "c-globex");
            _repo.Verify(r => r.RemoveRoleFromAllUsersAsync("manager", "globex"), Times.Once);
        }

        [Fact]
        public async Task PropagateRoleDelete_WithConsent_StillSkipsCopyWithChildRoles()
        {
            List<Role>? written = null;
            GivenSourceAndCopies(Copy("c-acme", "acme"), Copy("c-globex", "globex"));
            _repo.Setup(r => r.HasChildRolesAsync("manager", "globex")).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()))
                .Callback<List<Role>>(x => written = x).ReturnsAsync(true);

            await PropagateRoleDelete(confirmRevokeFromUsers: true);

            written!.Select(x => x.ItemId).Should().Equal("c-acme");
            _repo.Verify(r => r.RemoveRoleFromAllUsersAsync("manager", "globex"), Times.Never);
        }

        [Fact]
        public async Task PropagateRoleDelete_EventWithoutConsent_AppliesThePreExistingSkip()
        {
            List<Role>? written = null;
            GivenSourceAndCopies(Copy("c-acme", "acme"), Copy("c-globex", "globex"));
            _repo.Setup(r => r.HasUserAssignmentsAsync("manager", "globex")).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()))
                .Callback<List<Role>>(x => written = x).ReturnsAsync(true);

            // A message serialised before the consent field existed deserialises to false, so an
            // old or replayed message can only ever be less destructive, never more.
            await PropagateRoleDelete();

            written!.Select(x => x.ItemId).Should().Equal("c-acme");
        }

        [Fact]
        public async Task ArchivePermission_WithConsent_ScrubsDirectUserGrants()
        {
            InstallContext(orgId: "default");
            GivenPermission(ArchiveTarget());

            var result = await Create().ArchivePermissionAsync("p1", confirmRevokeFromUsers: true);

            result.IsSuccess.Should().BeTrue();
            // User.Permissions is the binding that actually mints a token claim; Permission.Roles
            // grants nothing on its own, so without this an archived permission keeps working.
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync("reports::export", "default"), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_WithoutConsent_LeavesDirectUserGrantsAlone()
        {
            InstallContext(orgId: "default");
            GivenPermission(ArchiveTarget());

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ArchivePermission_RemovesTheResourceFromTheSignUpDefaults_WithoutConsent()
        {
            InstallContext(orgId: "default");
            GivenPermission(ArchiveTarget());

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeTrue();
            // Deliberately NOT consent-gated: this pulls a dangling pointer out of configuration
            // rather than revoking anyone's access. Left behind, it would keep handing every new
            // signup a working grant on an archived permission, since
            // DefaultPermissionsForNewUserOnSignUp lands in User.Permissions.
            _repo.Verify(r => r.RemovePermissionFromSignUpDefaultsAsync("reports::export"), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_SignUpDefaultsCleanupUnacknowledged_LeavesPermissionActive()
        {
            InstallContext(orgId: "default");
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.RemovePermissionFromSignUpDefaultsAsync(It.IsAny<string>())).ReturnsAsync(false);

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeFalse();
            _repo.Verify(r => r.UpdatePermissionAsync(It.IsAny<Permission>()), Times.Never);
        }

        [Fact]
        public async Task ArchivePermission_WithConsent_UserCleanupUnacknowledged_LeavesPermissionActive()
        {
            InstallContext(orgId: "default");
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.RemovePermissionFromAllUsersAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            var result = await Create().ArchivePermissionAsync("p1", confirmRevokeFromUsers: true);

            result.IsSuccess.Should().BeFalse();
            _repo.Verify(r => r.UpdatePermissionAsync(It.IsAny<Permission>()), Times.Never);
        }

        [Fact]
        public async Task PropagatePermissionDelete_WithConsent_ScrubsDirectGrantsInEveryOrganization()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                ArchiveTarget(),
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme" },
                new() { ItemId = "p-globex", Resource = "reports::export", OrganizationId = "globex" }
            });
            _repo.Setup(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent
                {
                    Entity = "permission",
                    Action = "delete",
                    ItemId = "p1",
                    ConfirmRevokeFromUsers = true
                });

            // The invariant is per organization, not just the one the administrator was looking at:
            // a direct grant left behind anywhere keeps minting a claim for an archived permission.
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync("reports::export", "acme"), Times.Once);
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync("reports::export", "globex"), Times.Once);
            // The default-organization record is handled by ArchivePermissionAsync, never here.
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync("reports::export", "default"), Times.Never);
        }

        [Fact]
        public async Task PropagatePermissionDelete_WithConsent_CopiesAlreadyArchived_StillScrubsDirectGrants()
        {
            GivenPermission(ArchiveTarget());
            // The state production actually reaches: ArchivePermissionAsync calls
            // UpdateAllSamePermissionAsync, an UpdateMany filtered on Resource with no organization
            // clause, so every copy is already archived before this consumer runs. While the scrub
            // sat inside the archive loop it was skipped for all of them, and the direct grants --
            // the binding that mints a token claim -- survived the archive in every organization.
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                ArchiveTarget(),
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme", IsArchived = true },
                new() { ItemId = "p-globex", Resource = "reports::export", OrganizationId = "globex", IsArchived = true }
            });

            await Create().ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent
                {
                    Entity = "permission",
                    Action = "delete",
                    ItemId = "p1",
                    ConfirmRevokeFromUsers = true
                });

            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync("reports::export", "acme"), Times.Once);
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync("reports::export", "globex"), Times.Once);
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync("reports::export", "default"), Times.Never);
            // Nothing left to archive, and the scrub still had to run.
            _repo.Verify(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>()), Times.Never);
        }

        [Fact]
        public async Task PropagatePermissionDelete_WithConsent_UnacknowledgedScrub_StillScrubsTheRest()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                ArchiveTarget(),
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme" },
                new() { ItemId = "p-globex", Resource = "reports::export", OrganizationId = "globex" }
            });
            _repo.Setup(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemovePermissionFromAllUsersAsync("reports::export", "acme")).ReturnsAsync(false);

            await Create().ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent
                {
                    Entity = "permission",
                    Action = "delete",
                    ItemId = "p1",
                    ConfirmRevokeFromUsers = true
                });

            // Best-effort per organization: the archive has already committed, so one unacknowledged
            // scrub is logged and must not stop the others being cleaned.
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync("reports::export", "globex"), Times.Once);
        }

        [Fact]
        public async Task PropagatePermissionDelete_WithConsent_DuplicateOrgCopies_ScrubbedOnce()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                ArchiveTarget(),
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme" },
                new() { ItemId = "p-acme-dup", Resource = "reports::export", OrganizationId = "acme" }
            });
            _repo.Setup(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent
                {
                    Entity = "permission",
                    Action = "delete",
                    ItemId = "p1",
                    ConfirmRevokeFromUsers = true
                });

            // One organization's grants are pulled by resource, so a second copy from data drift is
            // wasted work rather than a wrong answer.
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync("reports::export", "acme"), Times.Once);
        }

        [Fact]
        public async Task PropagatePermissionDelete_WithoutConsent_LeavesDirectGrantsAlone()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                ArchiveTarget(),
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme" }
            });
            _repo.Setup(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent { Entity = "permission", Action = "delete", ItemId = "p1" });

            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ArchivePermission_WithConsent_StillRefusesBuiltInForNonRoot()
        {
            InstallContext(orgId: "default");
            var permission = ArchiveTarget();
            permission.IsBuiltIn = true;
            GivenPermission(permission);
            _iam.Setup(i => i.IsRoot()).Returns(false);

            var result = await Create().ArchivePermissionAsync("p1", confirmRevokeFromUsers: true);

            // Consent is not authorization.
            result.Errors["forbidden"].Should().Be("Only_Root_Tenant_Can_Archive_Built_In_Permission");
            _repo.Verify(r => r.RemovePermissionFromAllUsersAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ArchiveRole_WriteFails_CleanupStillRanButNoEventsSent()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.UpdateRoleAsync(It.IsAny<Role>())).ReturnsAsync(false);

            var result = await Create().ArchiveRoleAsync("r1");

            result.IsSuccess.Should().BeFalse();
            // Not "nothing happened": cleanup deliberately precedes the write, and A2 accepts that
            // half-state as the safer one. What must not happen is announcing the archive.
            _repo.Verify(r => r.RemoveRoleFromAllPermissionsAsync("manager", "default"), Times.Once);
            _repo.Verify(r => r.RemoveRoleFromAllUsersAsync("manager", "default"), Times.Once);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<ResourceMutationEvent>()), Times.Never);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Never);
        }

        [Fact]
        public async Task ArchiveRole_PermissionCleanupUnacknowledged_DoesNotArchive()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.RemoveRoleFromAllPermissionsAsync("manager", "default")).ReturnsAsync(false);

            var result = await Create().ArchiveRoleAsync("r1");

            // Archiving anyway would leave the state A2 calls unsafe: an archived role still
            // referenced by permissions.
            result.IsSuccess.Should().BeFalse();
            _repo.Verify(r => r.UpdateRoleAsync(It.IsAny<Role>()), Times.Never);
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<ResourceMutationEvent>()), Times.Never);
        }

        [Fact]
        public async Task ArchiveRole_UserCleanupUnacknowledged_DoesNotArchive()
        {
            GivenRole(ArchiveRoleTarget());
            _repo.Setup(r => r.RemoveRoleFromAllUsersAsync("manager", "default")).ReturnsAsync(false);

            var result = await Create().ArchiveRoleAsync("r1");

            result.IsSuccess.Should().BeFalse();
            _repo.Verify(r => r.UpdateRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        // ---------- Guard precedence ----------

        [Fact]
        public async Task ArchiveRole_ArchivedDefaultCopy_ReportsCopyGuardFirst()
        {
            InstallContext(orgId: "acme");
            GivenRole(ArchiveRoleTarget(org: "acme", createdFromDefault: true, isArchived: true));

            var result = await Create().ArchiveRoleAsync("r1");

            result.Errors.Should().NotContainKey("archived");
            result.Errors["forbidden"].Should().Be("Can_Not_Archive_Default_Copied_Role");
        }

        [Fact]
        public async Task ArchiveRole_ForeignOrgRoleWithChildren_ReportsOrgGuardFirst()
        {
            InstallContext(orgId: "acme");
            GivenRole(ArchiveRoleTarget(org: "globex"));
            _repo.Setup(r => r.HasChildRolesAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            var result = await Create().ArchiveRoleAsync("r1");

            result.Errors.Should().NotContainKey("dependency");
            result.Errors["forbidden"].Should().Be("Not_Allowed_To_Archive_Role_From_Another_Organization");
            _repo.Verify(r => r.HasChildRolesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- Guarding the archived state elsewhere ----------
        //
        // Archiving is only meaningful if the rest of the service respects it. Both paths below
        // resolve roles through the deliberately unfiltered slug lookup, so without these guards an
        // archived role stays fully reachable and this ticket's own invariants come undone.

        [Fact]
        public async Task SetRoles_ArchivedRole_IsRefusedSoCleanupIsNotUndone()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("manager", "default"))
                .ReturnsAsync(ArchiveRoleTarget(isArchived: true));

            var result = await Create().SetRolesAsync(new SetRolesRequest
            {
                Slug = "manager",
                AddPermissions = new List<string> { "p1" },
                RemovePermissions = new List<string>()
            });

            // Archiving pulls the slug from every permission in the org; allowing this call would
            // put it straight back.
            result.Errors.Should().ContainKey("archived");
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SetRoles_ActiveRole_StillProceeds()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("manager", "default"))
                .ReturnsAsync(ArchiveRoleTarget());

            var result = await Create().SetRolesAsync(new SetRolesRequest
            {
                Slug = "manager",
                AddPermissions = new List<string> { "p1" },
                RemovePermissions = new List<string>()
            });

            result.Errors.Should().NotContainKey("archived");
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), "default"), Times.Once);
        }

        [Fact]
        public async Task CreateRole_UnderArchivedParent_IsRefused()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("manager")).ReturnsAsync(ArchiveRoleTarget(isArchived: true));

            var result = await Create().CreateRoleAsync(RoleReq("lead", "manager"));

            // A live role hanging off a parent that no roles list shows is exactly the hierarchy
            // corruption the ticket's "block rather than guess" priority is about.
            result.IsSuccess.Should().BeFalse();
            result.Errors["ParentRoleSlug"].Should().Be("Parent_Role_Is_Archived");
            _repo.Verify(r => r.InsertRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task CreateRole_UnderActiveParent_StillSucceeds()
        {
            _repo.Setup(r => r.GetRoleBySlugAsync("manager"))
                .ReturnsAsync(new Role { Slug = "manager", Name = "M", Description = "d", ParentRoleSlug = null });

            var result = await Create().CreateRoleAsync(RoleReq("lead", "manager"));

            result.IsSuccess.Should().BeTrue();
        }

        // ---------- DeleteRoleForAllOrg, via the propagation consumer (H7, C9, C10) ----------

        private static Role Copy(string id, string org, bool isArchived = false) => new()
        {
            ItemId = id,
            Slug = "manager",
            Name = "Manager",
            Description = "d",
            OrganizationId = org,
            CreatedFromDefault = true,
            IsArchived = isArchived
        };

        private Task PropagateRoleDelete(string itemId = "r1", bool confirmRevokeFromUsers = false) =>
            Create().ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent
                {
                    Entity = "role",
                    Action = "delete",
                    ItemId = itemId,
                    ConfirmRevokeFromUsers = confirmRevokeFromUsers
                });

        private void GivenSourceAndCopies(params Role[] copies)
        {
            var source = ArchiveRoleTarget();
            source.LastUpdatedBy = "archiver-1";
            GivenRole(source);
            _repo.Setup(r => r.GetRolesBySlugAsync("manager"))
                .ReturnsAsync(new List<Role>(copies) { source });
        }

        [Fact]
        public async Task PropagateRoleDelete_ArchivesEveryCopyInOneBulkWriteExcludingTheSource()
        {
            List<Role>? written = null;
            GivenSourceAndCopies(Copy("c-acme", "acme"), Copy("c-globex", "globex"));
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()))
                .Callback<List<Role>>(x => written = x).ReturnsAsync(true);

            await PropagateRoleDelete();

            _repo.Verify(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()), Times.Once);
            written!.Select(x => x.ItemId).Should().BeEquivalentTo(new[] { "c-acme", "c-globex" });
            written.Should().OnlyContain(x => x.IsArchived);
            // The source is written by ArchiveRoleAsync; including it here would write it twice.
            written.Should().NotContain(x => x.ItemId == "r1");
            written.Should().OnlyContain(x => x.LastUpdatedBy == "archiver-1");

            _repo.Verify(r => r.RemoveRoleFromAllPermissionsAsync("manager", "acme"), Times.Once);
            _repo.Verify(r => r.RemoveRoleFromAllPermissionsAsync("manager", "globex"), Times.Once);
            _repo.Verify(r => r.RemoveRoleFromAllUsersAsync("manager", "acme"), Times.Once);
            _repo.Verify(r => r.RemoveRoleFromAllUsersAsync("manager", "globex"), Times.Once);
        }

        [Fact]
        public async Task PropagateRoleDelete_LeavesNonCopiesSharingTheSlugAlone()
        {
            List<Role>? written = null;
            var independent = Copy("c-other", "globex");
            independent.CreatedFromDefault = false;
            GivenSourceAndCopies(Copy("c-acme", "acme"), independent);
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()))
                .Callback<List<Role>>(x => written = x).ReturnsAsync(true);

            await PropagateRoleDelete();

            written!.Select(x => x.ItemId).Should().Equal("c-acme");
        }

        [Fact]
        public async Task PropagateRoleDelete_SkipsCopyWithActiveAssignmentsButArchivesTheRest()
        {
            List<Role>? written = null;
            GivenSourceAndCopies(Copy("c-acme", "acme"), Copy("c-globex", "globex"));
            _repo.Setup(r => r.HasUserAssignmentsAsync("manager", "globex")).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()))
                .Callback<List<Role>>(x => written = x).ReturnsAsync(true);

            await PropagateRoleDelete();

            // Partial propagation is the specified outcome: one busy organization must not veto a
            // platform-wide retirement, and must not have a live assignment orphaned either.
            written!.Select(x => x.ItemId).Should().Equal("c-acme");
            // A skipped copy is left completely untouched, not half-cleaned.
            _repo.Verify(r => r.RemoveRoleFromAllPermissionsAsync("manager", "globex"), Times.Never);
            _repo.Verify(r => r.RemoveRoleFromAllUsersAsync("manager", "globex"), Times.Never);
        }

        [Fact]
        public async Task PropagateRoleDelete_SkipsCopyWithChildRolesButArchivesTheRest()
        {
            List<Role>? written = null;
            GivenSourceAndCopies(Copy("c-acme", "acme"), Copy("c-globex", "globex"));
            _repo.Setup(r => r.HasChildRolesAsync("manager", "acme")).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()))
                .Callback<List<Role>>(x => written = x).ReturnsAsync(true);

            await PropagateRoleDelete();

            written!.Select(x => x.ItemId).Should().Equal("c-globex");
            _repo.Verify(r => r.RemoveRoleFromAllPermissionsAsync("manager", "acme"), Times.Never);
        }

        [Fact]
        public async Task PropagateRoleDelete_SkipsCopyWhosePermissionCleanupIsUnacknowledged()
        {
            List<Role>? written = null;
            GivenSourceAndCopies(Copy("c-acme", "acme"), Copy("c-globex", "globex"));
            _repo.Setup(r => r.RemoveRoleFromAllPermissionsAsync("manager", "acme")).ReturnsAsync(false);
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()))
                .Callback<List<Role>>(x => written = x).ReturnsAsync(true);

            await PropagateRoleDelete();

            written!.Select(x => x.ItemId).Should().Equal("c-globex");
        }

        [Fact]
        public async Task PropagateRoleDelete_SkipsCopyWhoseUserCleanupIsUnacknowledged()
        {
            List<Role>? written = null;
            GivenSourceAndCopies(Copy("c-acme", "acme"), Copy("c-globex", "globex"));
            _repo.Setup(r => r.RemoveRoleFromAllUsersAsync("manager", "globex")).ReturnsAsync(false);
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()))
                .Callback<List<Role>>(x => written = x).ReturnsAsync(true);

            await PropagateRoleDelete();

            written!.Select(x => x.ItemId).Should().Equal("c-acme");
        }

        [Fact]
        public async Task PropagateRoleDelete_AlreadyArchivedCopiesAreNotRewritten()
        {
            GivenSourceAndCopies(Copy("c-acme", "acme", isArchived: true));

            await PropagateRoleDelete();

            // A redelivered queue message must not rewrite settled copies.
            _repo.Verify(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()), Times.Never);
        }

        [Fact]
        public async Task PropagateRoleDelete_NoCopies_WritesNothing()
        {
            GivenSourceAndCopies();

            await PropagateRoleDelete();

            _repo.Verify(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()), Times.Never);
        }

        /// <summary>
        /// The consumer awaits this and ignores the result, and the ticket states events are never
        /// replayed, so an unacknowledged propagation write is not retried — a gap is repaired by a
        /// reconciliation pass, not by the queue. That makes the warning the only trace it leaves,
        /// so the test asserts the warning names the slug and the affected organization rather than
        /// merely asserting nothing was thrown, which would read as endorsing the silence.
        /// </summary>
        [Fact]
        public async Task PropagateRoleDelete_BulkWriteUnacknowledged_WarnsWithSlugAndOrganization()
        {
            var warnings = new List<string>();
            GivenSourceAndCopies(Copy("c-acme", "acme"));
            _repo.Setup(r => r.UpdateRolesAsync(It.IsAny<List<Role>>())).ReturnsAsync(false);

            await Create(WarningCapture(warnings).Object).ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent { Entity = "role", Action = "delete", ItemId = "r1" });

            _repo.Verify(r => r.UpdateRolesAsync(It.IsAny<List<Role>>()), Times.Once);
            warnings.Should().ContainSingle(w => w.Contains("manager") && w.Contains("acme"));
        }

        [Fact]
        public async Task PropagateRoleDelete_SkippedCopy_WarnsWithSlugAndOrganization()
        {
            var warnings = new List<string>();
            GivenSourceAndCopies(Copy("c-acme", "acme"), Copy("c-globex", "globex"));
            _repo.Setup(r => r.HasUserAssignmentsAsync("manager", "globex")).ReturnsAsync(true);

            await Create(WarningCapture(warnings).Object).ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent { Entity = "role", Action = "delete", ItemId = "r1" });

            // C9 makes the warning log the interface for reviewing skipped copies, so it has to
            // identify which copy in which organization was left behind.
            warnings.Should().ContainSingle(w => w.Contains("manager") && w.Contains("globex"));
        }

        // ---------- Role counts after a permission archive ----------

        /// <summary>
        /// Count is what a role GRANTS. Archiving a permission leaves the binding in place -- the
        /// Roles array IS the binding and pulling it would make the soft delete unrestorable -- so
        /// the number only becomes right again when it is recomputed.
        /// </summary>
        [Fact]
        public async Task ArchivePermission_RefreshesTheCountOfEveryRoleThatUsedIt()
        {
            var permission = ArchiveTarget();
            permission.Roles = new List<string> { "admin", "manager" };
            GivenPermission(permission);
            _repo.Setup(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            await Create().ArchivePermissionAsync("p1");

            _repo.Verify(r => r.UpdateRolesCountAsync("admin", "default"), Times.Once);
            _repo.Verify(r => r.UpdateRolesCountAsync("manager", "default"), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_DuplicateOrBlankSlugs_AreNotRecountedTwice()
        {
            var permission = ArchiveTarget();
            permission.Roles = new List<string> { "admin", "ADMIN", "  ", "admin" };
            GivenPermission(permission);
            _repo.Setup(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            await Create().ArchivePermissionAsync("p1");

            _repo.Verify(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        /// <summary>
        /// The archive has already committed by the time counts are refreshed, so a stale number
        /// must neither fail the request nor stop the remaining roles being corrected.
        /// </summary>
        [Fact]
        public async Task ArchivePermission_CountRefreshUnacknowledged_StillSucceedsAndCorrectsTheRest()
        {
            var permission = ArchiveTarget();
            permission.Roles = new List<string> { "admin", "manager" };
            GivenPermission(permission);
            _repo.Setup(r => r.UpdateRolesCountAsync("admin", "default")).ReturnsAsync(false);
            _repo.Setup(r => r.UpdateRolesCountAsync("manager", "default")).ReturnsAsync(true);

            var result = await Create().ArchivePermissionAsync("p1");

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdateRolesCountAsync("manager", "default"), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_NoRolesReferenceIt_RefreshesNothing()
        {
            var permission = ArchiveTarget();
            permission.Roles = new List<string>();
            GivenPermission(permission);

            await Create().ArchivePermissionAsync("p1");

            _repo.Verify(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task PropagatePermissionDelete_RefreshesRoleCountsInEveryOtherOrganization()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                ArchiveTarget(),
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme", Roles = new List<string> { "admin" } },
                new() { ItemId = "p-globex", Resource = "reports::export", OrganizationId = "globex", Roles = new List<string> { "manager" } }
            });
            _repo.Setup(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent { Entity = "permission", Action = "delete", ItemId = "p1" });

            // Each organization is corrected against ITS OWN copy's Roles array: the same resource
            // can be bound to different roles in different organizations.
            _repo.Verify(r => r.UpdateRolesCountAsync("admin", "acme"), Times.Once);
            _repo.Verify(r => r.UpdateRolesCountAsync("manager", "globex"), Times.Once);
        }

        /// <summary>
        /// The case that makes this fix necessary rather than cosmetic. ArchivePermissionAsync
        /// calls UpdateAllSamePermissionAsync, which filters on Resource with NO organization
        /// clause, so by the time this consumer runs every copy is usually archived already and the
        /// archive loop skips all of them. Their roles still advertise the permission, and this is
        /// the only place that corrects them -- so the refresh must not be gated on having archived
        /// anything here.
        /// </summary>
        [Fact]
        public async Task PropagatePermissionDelete_CopiesAlreadyArchived_StillRefreshesTheirRoleCounts()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                ArchiveTarget(),
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme", IsArchived = true, Roles = new List<string> { "admin" } }
            });
            _repo.Setup(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent { Entity = "permission", Action = "delete", ItemId = "p1" });

            // Nothing was archived by this run -- and the count still had to be corrected.
            _repo.Verify(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>()), Times.Never);
            _repo.Verify(r => r.UpdateRolesCountAsync("admin", "acme"), Times.Once);
        }

        [Fact]
        public async Task PropagatePermissionDelete_LeavesTheDefaultOrganizationsCountsToTheArchiveItself()
        {
            GivenPermission(ArchiveTarget());
            _repo.Setup(r => r.GetPermissionsByResourceAsync("reports::export")).ReturnsAsync(new List<Permission>
            {
                ArchiveTarget(),
                new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme", Roles = new List<string> { "admin" } }
            });
            _repo.Setup(r => r.UpdatePermissionsAsync(It.IsAny<List<Permission>>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            await Create().ExecutePropagationRolePermissionUpdateAsync(
                new PropagationRolePermissionUpdateEvent { Entity = "permission", Action = "delete", ItemId = "p1" });

            // ArchivePermissionAsync already did it, synchronously, and does so even for a
            // single-organization tenant that never reaches this propagation at all.
            _repo.Verify(r => r.UpdateRolesCountAsync(It.IsAny<string>(), "default"), Times.Never);
        }
    }
}
