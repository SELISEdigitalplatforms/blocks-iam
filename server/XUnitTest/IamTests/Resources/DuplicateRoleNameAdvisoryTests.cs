using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    /// <summary>
    /// SPEC12 — the duplicate-name advisory. Covers H1-H3, H6 and C1-C5: a default-organization
    /// administrator is told, as a count, that other organizations already use the name, and must
    /// confirm once; a child-organization caller is never told anything.
    /// </summary>
    public class DuplicateRoleNameAdvisoryTests : IDisposable
    {
        private const string OrgA = "f47ac10b-58cc-4372-a567-0e02b2c3d479";

        private readonly Mock<IResourceRepository> _repo = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();
        private readonly Mock<IValidator<CreatePermissionRequest>> _permValidator = new();
        private readonly Mock<IValidator<UpdatePermissionRequest>> _updatePermValidator = new();
        private readonly Mock<IValidator<CreateRoleRequest>> _roleValidator = new();
        private readonly Mock<ITenantPermissionPropagator> _propagator = new();
        private readonly Mock<IUserActivityDispatcher> _activity = new();

        public DuplicateRoleNameAdvisoryTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext("default");

            _roleValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.InsertRoleAsync(It.IsAny<Role>())).ReturnsAsync(true);
            _repo.Setup(r => r.GetOrganizationById(It.IsAny<string>()))
                .ReturnsAsync((string id) => new Organization { ItemId = id, Name = "O", IsDisabled = false });
            _repo.Setup(r => r.GetRolesBySlugAsync(It.IsAny<string>())).ReturnsAsync(new List<Role>());
            _repo.Setup(r => r.HasOwnedRoleWithNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _repo.Setup(r => r.GetOwnedRolesWithNameInOtherOrganizationsAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new List<Role>());
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

        private static CreateRoleRequest RoleReq(bool confirm = false) => new()
        {
            Name = "Manager", Slug = "manager", Description = "d", ConfirmDuplicateName = confirm
        };

        private void GivenOtherOrganizationsOwningManager(params (string Org, string Slug)[] rows)
        {
            _repo.Setup(r => r.GetOwnedRolesWithNameInOtherOrganizationsAsync("Manager", "default"))
                .ReturnsAsync(rows.Select(x => new Role
                {
                    ItemId = "r-" + x.Org, Name = "Manager", Slug = x.Slug, OrganizationId = x.Org
                }).ToList());
        }

        // ---------- H1: no collision, one round trip, no dialog ----------

        [Fact]
        public async Task Create_NoOtherOrganizationUsesTheName_SucceedsWithoutConfirmation()
        {
            var result = await Create().CreateRoleAsync(RoleReq());

            result.IsSuccess.Should().BeTrue();
            var typed = result.Should().BeOfType<CreateRoleResponse>().Subject;
            typed.RequiresDuplicateNameConfirmation.Should().BeFalse();
            typed.DuplicateNameOrganizationCount.Should().Be(0);
        }

        // ---------- H2: collision refuses and reports counts ----------

        [Fact]
        public async Task Create_TwoOtherOrganizationsOwnTheName_IsRefusedWithTheCount()
        {
            GivenOtherOrganizationsOwningManager((OrgA, "manager_f47ac10b"), ("org-b", "manager_bbbbbbbb"));

            var result = await Create().CreateRoleAsync(RoleReq());

            var typed = result.Should().BeOfType<CreateRoleResponse>().Subject;
            typed.IsSuccess.Should().BeFalse();
            typed.RequiresDuplicateNameConfirmation.Should().BeTrue();
            typed.DuplicateNameOrganizationCount.Should().Be(2);
            typed.SlugConflictOrganizationCount.Should().Be(0);
            typed.Errors!["duplicate_name"].Should().Be("Role_Name_Exists_In_Other_Organizations");
            _repo.Verify(r => r.InsertRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        [Fact]
        public async Task Create_SeveralRolesInOneOrganization_CountsTheOrganizationOnce()
        {
            GivenOtherOrganizationsOwningManager((OrgA, "manager_f47ac10b"), (OrgA, "manager-2_f47ac10b"));

            var result = await Create().CreateRoleAsync(RoleReq());

            result.Should().BeOfType<CreateRoleResponse>()
                .Subject.DuplicateNameOrganizationCount.Should().Be(1);
        }

        // ---------- H2 + slug conflict: the only "will not receive" signal ----------

        [Fact]
        public async Task Create_AnotherOrganizationHoldsTheSameBareSlug_IsCountedSeparately()
        {
            GivenOtherOrganizationsOwningManager((OrgA, "manager"), ("org-b", "manager_bbbbbbbb"));

            var result = await Create().CreateRoleAsync(RoleReq());

            var typed = result.Should().BeOfType<CreateRoleResponse>().Subject;
            typed.DuplicateNameOrganizationCount.Should().Be(2);
            typed.SlugConflictOrganizationCount.Should().Be(1);
        }

        // ---------- H3: confirming proceeds and still propagates ----------

        [Fact]
        public async Task Create_Confirmed_CreatesAndPropagates()
        {
            GivenOtherOrganizationsOwningManager((OrgA, "manager_f47ac10b"));

            var result = await Create().CreateRoleAsync(RoleReq(confirm: true));

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.InsertRoleAsync(It.Is<Role>(x => x.Slug == "manager")), Times.Once);
            _iam.Verify(i => i.SendToQueueAsync(
                It.IsAny<string>(), It.IsAny<PropagationRolePermissionUpdateEvent>()), Times.Once);
        }

        [Fact]
        public async Task Create_Confirmed_DoesNotEvenRunTheCountQuery()
        {
            await Create().CreateRoleAsync(RoleReq(confirm: true));

            _repo.Verify(r => r.GetOwnedRolesWithNameInOtherOrganizationsAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- C2: the flag is inert when there is no collision ----------

        [Fact]
        public async Task Create_ConfirmedWithNoCollision_SucceedsRatherThanErroring()
        {
            var result = await Create().CreateRoleAsync(RoleReq(confirm: true));

            result.IsSuccess.Should().BeTrue();
        }

        // ---------- H6: a child organization is told nothing ----------

        [Fact]
        public async Task Create_FromChildOrganization_NeverRunsTheAdvisory()
        {
            InstallContext(OrgA);
            GivenOtherOrganizationsOwningManager(("org-b", "manager_bbbbbbbb"));

            var result = await Create().CreateRoleAsync(RoleReq());

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.GetOwnedRolesWithNameInOtherOrganizationsAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- C1: own-organization rules decide first ----------

        [Fact]
        public async Task Create_OwnOrganizationAlreadyOwnsTheName_DoesNotRunTheAdvisory()
        {
            _repo.Setup(r => r.HasOwnedRoleWithNameAsync("Manager", "default", null)).ReturnsAsync(true);
            GivenOtherOrganizationsOwningManager((OrgA, "manager_f47ac10b"));

            var result = await Create().CreateRoleAsync(RoleReq());

            result.Errors!["Name"].Should().Be("Role_Name_Already_Exists_In_Organization");
            _repo.Verify(r => r.GetOwnedRolesWithNameInOtherOrganizationsAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- C3: a failed count refuses rather than proceeding ----------

        [Fact]
        public async Task Create_CountQueryFails_RefusesRatherThanCreating()
        {
            _repo.Setup(r => r.GetOwnedRolesWithNameInOtherOrganizationsAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new TimeoutException("mongo down"));

            var result = await Create().CreateRoleAsync(RoleReq());

            var typed = result.Should().BeOfType<CreateRoleResponse>().Subject;
            typed.IsSuccess.Should().BeFalse();
            typed.RequiresDuplicateNameConfirmation.Should().BeTrue();
            _repo.Verify(r => r.InsertRoleAsync(It.IsAny<Role>()), Times.Never);
        }

        // ---------- C4: multi-org off means no advisory at all ----------

        [Fact]
        public async Task Create_MultiOrgDisabled_SkipsTheAdvisory()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = false });
            GivenOtherOrganizationsOwningManager((OrgA, "manager_f47ac10b"));

            var result = await Create().CreateRoleAsync(RoleReq());

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.GetOwnedRolesWithNameInOtherOrganizationsAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- C5: counts only, no identities ----------

        [Fact]
        public async Task Create_Advisory_CarriesNoOrganizationIdentity()
        {
            GivenOtherOrganizationsOwningManager((OrgA, "manager_f47ac10b"), ("org-b", "manager_bbbbbbbb"));

            var result = await Create().CreateRoleAsync(RoleReq());

            var serialized = System.Text.Json.JsonSerializer.Serialize((CreateRoleResponse)result);
            serialized.Should().NotContain(OrgA);
            serialized.Should().NotContain("org-b");
            serialized.Should().NotContain("manager_f47ac10b");
        }
    }
}
