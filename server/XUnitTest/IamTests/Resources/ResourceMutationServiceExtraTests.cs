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

        // ---------- ProcessPermissionAsync with propagation -> PropagateSetPermissionsAsync ----------

        [Fact]
        public async Task ProcessPermission_PropagationEnabled_ResolvesAndUpdatesAcrossOrgs()
        {
            _repo.Setup(r => r.UpdateRolesCountAsync("admin", It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .ReturnsAsync(new GetOrganizationsResponse
                {
                    Organizations = new List<Organization> { new() { ItemId = "o1", Name = "O1" } }
                });

            // add permission "addP" resolves to resource "res-add"; remove "rmP" -> "res-rm"
            _repo.Setup(r => r.GetPermissionsByIdsAsync(It.Is<List<string>>(l => l.Contains("addP"))))
                .ReturnsAsync(new List<Permission> { new() { ItemId = "addP", Name = "A", Resource = "res-add" } });
            _repo.Setup(r => r.GetPermissionsByIdsAsync(It.Is<List<string>>(l => l.Contains("rmP"))))
                .ReturnsAsync(new List<Permission> { new() { ItemId = "rmP", Name = "R", Resource = "res-rm" } });

            _repo.Setup(r => r.GetPermissionsByResourcesAsync(It.Is<List<string>>(l => l.Contains("res-add")), "o1"))
                .ReturnsAsync(new List<Permission> { new() { ItemId = "org-add-perm", Name = "A", Resource = "res-add", OrganizationId = "o1" } });
            _repo.Setup(r => r.GetPermissionsByResourcesAsync(It.Is<List<string>>(l => l.Contains("res-rm")), "o1"))
                .ReturnsAsync(new List<Permission> { new() { ItemId = "org-rm-perm", Name = "R", Resource = "res-rm", OrganizationId = "o1" } });

            _repo.Setup(r => r.UpdateRolePermissionByIdsAsync("admin", It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.RemoveRolePermissionByIdsAsync("admin", It.IsAny<List<string>>(), It.IsAny<string>())).ReturnsAsync(true);

            var ok = await Create().ProcessPermissionAsync(new ResourceSetToPermissionMutationEvent
            {
                Entity = ResourceEntity.Role,
                Slug = "admin",
                AddPermissions = new List<string> { "addP" },
                RemovePermissions = new List<string> { "rmP" },
                IsPropagationEnable = true
            });

            ok.Should().BeTrue();
            _repo.Verify(r => r.UpdateRolesCountAsync("admin", It.IsAny<string>()), Times.Once);
            _repo.Verify(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()), Times.Once);
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync("admin", It.Is<List<string>>(l => l.Contains("org-add-perm")), It.IsAny<string>()), Times.Once);
            _repo.Verify(r => r.RemoveRolePermissionByIdsAsync("admin", It.Is<List<string>>(l => l.Contains("org-rm-perm")), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ProcessPermission_PropagationEnabled_NoOrganizations_SkipsRoleUpdates()
        {
            _repo.Setup(r => r.UpdateRolesCountAsync("admin", It.IsAny<string>())).ReturnsAsync(true);
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .ReturnsAsync(new GetOrganizationsResponse { Organizations = new List<Organization>() });

            var ok = await Create().ProcessPermissionAsync(new ResourceSetToPermissionMutationEvent
            {
                Entity = ResourceEntity.Role,
                Slug = "admin",
                AddPermissions = new List<string> { "addP" },
                RemovePermissions = new List<string>(),
                IsPropagationEnable = true
            });

            ok.Should().BeTrue();
            _repo.Verify(r => r.UpdateRolePermissionByIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.GetPermissionsByIdsAsync(It.IsAny<List<string>>()), Times.Never);
        }

        // ---------- ExecuteOrganizationProvisioningAsync -> CopyPermissionsFromDefault batch loop ----------

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

        // ---------- SetRolesAsync happy path with propagation enabled ----------

        [Fact]
        public async Task SetRoles_PropagationEnabled_MarksEventForPropagation()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
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
            _iam.Verify(i => i.SendToQueueAsync(It.IsAny<string>(),
                It.Is<ResourceSetToPermissionMutationEvent>(e => e.IsPropagationEnable && e.Slug == "admin")), Times.Once);
        }
    }
}
