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
using Microsoft.Extensions.Logging;
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

        private ResourceMutationService Create(ILogger<ResourceMutationService> logger) =>
            new(logger, _repo.Object, _iam.Object,
                _permValidator.Object, _updatePermValidator.Object, _roleValidator.Object,
                _propagator.Object, _activity.Object);

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

        // ---------- Consented assignment propagation (#466) ----------

        /// <summary>
        /// Wires a two-organization tenant where each organization holds its OWN permission id for
        /// the same Resource. That difference is the point: an implementation that propagated the
        /// caller's raw ItemIds would bind nothing, because a permission id is per-organization.
        /// </summary>
        private void GivenTwoOrgsHoldingTheSameResource(bool multiOrg = true)
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = multiOrg });
            _repo.Setup(r => r.GetAllOrgIdsAsync()).ReturnsAsync(new List<string> { "acme", "globex" });
            _repo.Setup(r => r.GetPermissionsByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync(new List<Permission> { new() { ItemId = "p-default", Resource = "reports::export" } });
            _repo.Setup(r => r.GetRoleBySlugAsync("manager", It.IsAny<string>()))
                .ReturnsAsync((string slug, string org) => new Role { ItemId = "r-" + org, Slug = slug, OrganizationId = org });
            _repo.Setup(r => r.GetPermissionsByResourcesAsync(It.IsAny<List<string>>(), "acme"))
                .ReturnsAsync(new List<Permission> { new() { ItemId = "p-acme", Resource = "reports::export", OrganizationId = "acme" } });
            _repo.Setup(r => r.GetPermissionsByResourcesAsync(It.IsAny<List<string>>(), "globex"))
                .ReturnsAsync(new List<Permission> { new() { ItemId = "p-globex", Resource = "reports::export", OrganizationId = "globex" } });
            _repo.Setup(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.UpdateRolesCountAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        }

        private static ResourceSetToPermissionMutationEvent SetPermissionsEvent(
            bool propagate, string orgId = "default", List<string>? add = null, List<string>? remove = null) => new()
            {
                Entity = ResourceEntity.Role,
                Slug = "manager",
                OrganizationId = orgId,
                AddPermissions = add ?? new List<string> { "p-default" },
                RemovePermissions = remove ?? new List<string>(),
                PropagateToAllOrganizations = propagate
            };

        [Fact]
        public async Task ProcessPermission_WithConsent_BindsEachOrganizationsOwnPermissionId()
        {
            GivenTwoOrgsHoldingTheSameResource();

            await Create().ProcessPermissionAsync(SetPermissionsEvent(propagate: true));

            // Each organization gets ITS OWN id, resolved via the Resource string. The default
            // organization's id does not exist in acme or globex, so passing it through would
            // silently bind nothing.
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.Is<List<string>>(x => x.Contains("p-acme")), "acme"), Times.Once);
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.Is<List<string>>(x => x.Contains("p-globex")), "globex"), Times.Once);
        }

        [Fact]
        public async Task ProcessPermission_WithoutConsent_PropagatesNothing()
        {
            GivenTwoOrgsHoldingTheSameResource();

            await Create().ProcessPermissionAsync(SetPermissionsEvent(propagate: false));

            // The regression guard for the whole phase: omitting the flag must behave exactly as
            // the system did before it existed.
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.GetAllOrgIdsAsync(), Times.Never);
        }

        [Fact]
        public async Task ProcessPermission_MultiOrgDisabled_PropagatesNothingEvenWithConsent()
        {
            GivenTwoOrgsHoldingTheSameResource(multiOrg: false);

            await Create().ProcessPermissionAsync(SetPermissionsEvent(propagate: true));

            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessPermission_NonDefaultOrgCaller_PropagatesNothingEvenWithConsent()
        {
            GivenTwoOrgsHoldingTheSameResource();

            await Create().ProcessPermissionAsync(SetPermissionsEvent(propagate: true, orgId: "acme"));

            // An organization-scoped caller must never be able to rewrite bindings platform-wide.
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessPermission_WithConsent_RemovalsPropagateToo()
        {
            GivenTwoOrgsHoldingTheSameResource();

            await Create().ProcessPermissionAsync(SetPermissionsEvent(
                propagate: true, add: new List<string>(), remove: new List<string> { "p-default" }));

            _repo.Verify(r => r.RemoveRolePermissionByIdsAsync("manager", It.Is<List<string>>(x => x.Contains("p-acme")), "acme"), Times.Once);
            _repo.Verify(r => r.RemoveRolePermissionByIdsAsync("manager", It.Is<List<string>>(x => x.Contains("p-globex")), "globex"), Times.Once);
        }

        [Fact]
        public async Task ProcessPermission_WithConsent_SkipsOrganizationWhoseCopyIsArchived()
        {
            GivenTwoOrgsHoldingTheSameResource();
            _repo.Setup(r => r.GetRoleBySlugAsync("manager", "globex"))
                .ReturnsAsync(new Role { ItemId = "r-globex", Slug = "manager", OrganizationId = "globex", IsArchived = true });

            await Create().ProcessPermissionAsync(SetPermissionsEvent(propagate: true));

            // Propagation must not repopulate bindings on a role that was retired there --
            // SetRoleAsync refuses exactly this in the caller's own organization.
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), "acme"), Times.Once);
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), "globex"), Times.Never);
        }

        [Fact]
        public async Task ProcessPermission_WithConsent_SkipsOrganizationMissingThePermissionCopy()
        {
            GivenTwoOrgsHoldingTheSameResource();
            _repo.Setup(r => r.GetPermissionsByResourcesAsync(It.IsAny<List<string>>(), "globex"))
                .ReturnsAsync(new List<Permission>());

            await Create().ProcessPermissionAsync(SetPermissionsEvent(propagate: true));

            // Drift in one organization must not veto the others.
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), "acme"), Times.Once);
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), "globex"), Times.Never);
        }

        [Fact]
        public async Task ProcessPermission_StillWritesActivityAndRoleCount_WhetherOrNotItPropagates()
        {
            GivenTwoOrgsHoldingTheSameResource();

            await Create().ProcessPermissionAsync(SetPermissionsEvent(propagate: true));

            // Propagation runs last precisely so it cannot suppress the audit trail of what the
            // administrator did in their own organization.
            _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Event == "ROLE_PERMISSIONS_UPDATED")), Times.Once);
            _repo.Verify(r => r.UpdateRolesCountAsync("manager", "default"), Times.Once);
        }

        [Fact]
        public async Task ProcessPermission_UnacknowledgedWriteForOneOrg_WarnsAndStillProcessesTheRest()
        {
            var warnings = new List<string>();
            GivenTwoOrgsHoldingTheSameResource();
            _repo.Setup(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), "acme"))
                .ReturnsAsync(false);

            await Create(WarningCapture(warnings).Object).ProcessPermissionAsync(SetPermissionsEvent(propagate: true));

            // One organization failing must not abort the others, and the failure has to be
            // visible: an unacknowledged write leaves that organization silently out of step and
            // the log is the only place that surfaces.
            warnings.Should().ContainSingle(w => w.Contains("manager") && w.Contains("acme"));
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("manager", It.IsAny<List<string>>(), "globex"), Times.Once);
        }

        [Fact]
        public async Task SetRole_CarriesTheConsentFlagOntoTheEvent()
        {
            ResourceSetToPermissionMutationEvent? sent = null;
            _repo.Setup(r => r.GetRoleBySlugAsync("manager", It.IsAny<string>()))
                .ReturnsAsync(new Role { ItemId = "r1", Slug = "manager", OrganizationId = "default" });
            _repo.Setup(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);
            _iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<ResourceSetToPermissionMutationEvent>()))
                .Callback<string, ResourceSetToPermissionMutationEvent>((_, e) => sent = e)
                .Returns(Task.CompletedTask);

            await Create().SetRolesAsync(new SetRolesRequest
            {
                Slug = "manager",
                AddPermissions = new List<string> { "p-default" },
                PropagateToAllOrganizations = true
            });

            sent!.PropagateToAllOrganizations.Should().BeTrue();
        }
    }
}
