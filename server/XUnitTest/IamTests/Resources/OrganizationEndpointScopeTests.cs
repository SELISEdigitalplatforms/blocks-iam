using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
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
    /// The organization endpoints themselves used to apply no organization scope: the permission
    /// claim is minted per organization, so it means "you may administer organizations HERE", but
    /// the read filtered on the id alone and the list used an empty filter. A caller granted the
    /// permission in one organization could read and write every other organization in the tenant.
    /// <para>
    /// These tests pin the closed behaviour, and specifically that the by-id endpoints DENY rather
    /// than silently retarget. Retargeting is what ResourceWriteOrganizationScope does for a payload
    /// field that narrows a query; applied to a route id that names the write target it would
    /// rewrite the caller's own organization with a payload meant for another and answer success.
    /// </para>
    /// </summary>
    public class OrganizationEndpointScopeTests : IDisposable
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

        public OrganizationEndpointScopeTests()
        {
            BlocksContext.IsTestMode = true;
            InstallContext();

            _repo.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new TenantConfiguration { IsMultiOrgEnabled = true });
            _repo.Setup(r => r.SaveOrganizationAsync(It.IsAny<Organization>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .ReturnsAsync(new GetOrganizationsResponse { IsSuccess = true, Organizations = [], TotalCount = 0 });
            _iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            _activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);

            // Every organization named here exists unless a test says otherwise, so a "not found"
            // in these tests can only have come from the scope check.
            _repo.Setup(r => r.GetOrganizationById(It.IsAny<string>()))
                .ReturnsAsync((string id) => new Organization { ItemId = id, Name = id });
        }

        private static void InstallContext(string? orgId = "default")
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

        // ---------- GetOrganizationAsync ----------

        [Fact]
        public async Task GetOrganization_ScopedCaller_ReadingAnotherOrganization_IsNotFound()
        {
            InstallContext(OrgA);

            var result = await Create().GetOrganizationAsync(OrgB);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("not_found");
            result.Organization.Should().BeNull();
        }

        [Fact] // The document must not even be read: an out-of-scope id is answered before the lookup.
        public async Task GetOrganization_ScopedCaller_ReadingAnotherOrganization_DoesNotTouchTheRepository()
        {
            InstallContext(OrgA);

            await Create().GetOrganizationAsync(OrgB);

            _repo.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetOrganization_ScopedCaller_ReadingItsOwnOrganization_Succeeds()
        {
            InstallContext(OrgA);

            var result = await Create().GetOrganizationAsync(OrgA);

            result.IsSuccess.Should().BeTrue();
            result.Organization!.ItemId.Should().Be(OrgA);
        }

        [Fact]
        public async Task GetOrganization_TenantWideCaller_ReadsAnyOrganization()
        {
            var result = await Create().GetOrganizationAsync(OrgB);

            result.IsSuccess.Should().BeTrue();
            result.Organization!.ItemId.Should().Be(OrgB);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("no-org")]
        public async Task GetOrganization_CallerWithNoOrganization_IsDenied(string? tokenOrganizationId)
        {
            InstallContext(tokenOrganizationId);

            var result = await Create().GetOrganizationAsync(OrgA);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("organization_scope_denied");
        }

        // ---------- GetOrganizationsAsync ----------

        [Fact] // Here the organization IS a query filter, so the requested ids are discarded, not rejected.
        public async Task GetOrganizations_ScopedCaller_IsPinnedToItsOwnOrganization()
        {
            InstallContext(OrgA);
            GetOrganizationsRequest? seen = null;
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .Callback((GetOrganizationsRequest r) => seen = r)
                .ReturnsAsync(new GetOrganizationsResponse { IsSuccess = true, Organizations = [], TotalCount = 0 });

            await Create().GetOrganizationsAsync(new GetOrganizationsRequest
            {
                Filter = new GetOrganizationsFilter { Ids = [OrgB] }
            });

            seen!.Filter!.Ids.Should().Equal(OrgA);
        }

        [Fact] // A caller that asked for nothing still gets pinned, rather than the whole tenant.
        public async Task GetOrganizations_ScopedCallerWithNoFilter_IsStillPinned()
        {
            InstallContext(OrgA);
            GetOrganizationsRequest? seen = null;
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .Callback((GetOrganizationsRequest r) => seen = r)
                .ReturnsAsync(new GetOrganizationsResponse { IsSuccess = true, Organizations = [], TotalCount = 0 });

            await Create().GetOrganizationsAsync(new GetOrganizationsRequest());

            seen!.Filter!.Ids.Should().Equal(OrgA);
        }

        [Fact]
        public async Task GetOrganizations_TenantWideCaller_QueryIsLeftAlone()
        {
            GetOrganizationsRequest? seen = null;
            _repo.Setup(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()))
                .Callback((GetOrganizationsRequest r) => seen = r)
                .ReturnsAsync(new GetOrganizationsResponse { IsSuccess = true, Organizations = [], TotalCount = 0 });

            await Create().GetOrganizationsAsync(new GetOrganizationsRequest());

            seen!.Filter.Should().BeNull();
        }

        [Fact]
        public async Task GetOrganizations_CallerWithNoOrganization_IsDeniedAndNeverQueries()
        {
            InstallContext("no-org");

            var result = await Create().GetOrganizationsAsync(new GetOrganizationsRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("organization_scope_denied");
            _repo.Verify(r => r.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>()), Times.Never);
        }

        // ---------- UpdateOrganizationAsync ----------

        [Fact] // The case that makes deny-rather-than-retarget necessary.
        public async Task UpdateOrganization_ScopedCaller_TargetingAnotherOrganization_IsNotFoundAndWritesNothing()
        {
            InstallContext(OrgA);

            var result = await Create().UpdateOrganizationAsync(OrgB, new SaveOrganizationRequest { Name = "Hijacked" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("not_found");
            _repo.Verify(r => r.SaveOrganizationAsync(It.IsAny<Organization>()), Times.Never);
        }

        [Fact] // Specifically: the caller's OWN organization must not be rewritten in its place.
        public async Task UpdateOrganization_ScopedCaller_TargetingAnotherOrganization_DoesNotRetargetToItsOwn()
        {
            InstallContext(OrgA);

            await Create().UpdateOrganizationAsync(OrgB, new SaveOrganizationRequest { Name = "Hijacked" });

            _repo.Verify(r => r.GetOrganizationById(It.IsAny<string>()), Times.Never);
            _repo.Verify(r => r.SaveOrganizationAsync(It.Is<Organization>(o => o.ItemId == OrgA)), Times.Never);
        }

        [Fact]
        public async Task UpdateOrganization_ScopedCaller_UpdatingItsOwnOrganization_Succeeds()
        {
            InstallContext(OrgA);
            var stored = new Organization { ItemId = OrgA, Name = "Old" };
            _repo.Setup(r => r.GetOrganizationById(OrgA)).ReturnsAsync(stored);

            var result = await Create().UpdateOrganizationAsync(OrgA, new SaveOrganizationRequest { Name = "New" });

            result.IsSuccess.Should().BeTrue();
            stored.Name.Should().Be("New");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("no-org")]
        public async Task UpdateOrganization_CallerWithNoOrganization_IsDenied(string? tokenOrganizationId)
        {
            InstallContext(tokenOrganizationId);

            var result = await Create().UpdateOrganizationAsync(OrgA, new SaveOrganizationRequest { Name = "N" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("organization_scope_denied");
            _repo.Verify(r => r.SaveOrganizationAsync(It.IsAny<Organization>()), Times.Never);
        }

        [Fact] // "default" is a sentinel, not a document; say so instead of "wrong id".
        public async Task UpdateOrganization_TargetingTheDefaultOrganization_IsRejected()
        {
            var result = await Create().UpdateOrganizationAsync("default", new SaveOrganizationRequest { Name = "N" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("default_organization_immutable");
            _repo.Verify(r => r.SaveOrganizationAsync(It.IsAny<Organization>()), Times.Never);
        }

        // ---------- The member-grant fields on update ----------

        [Fact]
        public async Task UpdateOrganization_ScopedCaller_CannotSetTheDefaultGrantsForNewMembers()
        {
            InstallContext(OrgA);
            var stored = new Organization
            {
                ItemId = OrgA,
                Name = "Acme",
                DefaultRoleForMembers = ["member"],
                DefaultPermissionsForMembers = ["blocks-iam::iam::users"]
            };
            _repo.Setup(r => r.GetOrganizationById(OrgA)).ReturnsAsync(stored);

            var result = await Create().UpdateOrganizationAsync(OrgA, new SaveOrganizationRequest
            {
                Name = "Acme Renamed",
                DefaultRoleForMembers = ["admin"],
                DefaultPermissionsForMembers = ["blocks-iam::iam::mutate-users"]
            });

            // Dropped, not rejected: the rest of the payload still applies.
            result.IsSuccess.Should().BeTrue();
            stored.Name.Should().Be("Acme Renamed");
            stored.DefaultRoleForMembers.Should().Equal("member");
            stored.DefaultPermissionsForMembers.Should().Equal("blocks-iam::iam::users");
        }

        [Fact]
        public async Task UpdateOrganization_TenantWideCaller_CanSetTheDefaultGrantsForNewMembers()
        {
            var stored = new Organization
            {
                ItemId = OrgA,
                Name = "Acme",
                DefaultRoleForMembers = ["member"],
                DefaultPermissionsForMembers = ["blocks-iam::iam::users"]
            };
            _repo.Setup(r => r.GetOrganizationById(OrgA)).ReturnsAsync(stored);

            var result = await Create().UpdateOrganizationAsync(OrgA, new SaveOrganizationRequest
            {
                Name = "Acme",
                DefaultRoleForMembers = ["admin"],
                DefaultPermissionsForMembers = ["blocks-iam::iam::mutate-users"]
            });

            result.IsSuccess.Should().BeTrue();
            stored.DefaultRoleForMembers.Should().Equal("admin");
            stored.DefaultPermissionsForMembers.Should().Equal("blocks-iam::iam::mutate-users");
        }

        // ---------- organizations/my is deliberately NOT scoped ----------

        [Fact]
        public async Task GetMyOrganization_ListsEveryMembership_NotJustTheTokenOrganization()
        {
            // The switcher is driven by membership, not by the organization the caller is currently
            // acting in. Scoping this endpoint the way the others are scoped would return one
            // organization and make switching impossible.
            InstallContext(OrgA);
            _repo.Setup(r => r.GetOrganizationIdsByUserIdAsync("actor-1")).ReturnsAsync([OrgA, OrgB]);
            _repo.Setup(r => r.GetOrganizationsByIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync([
                    new Organization { ItemId = OrgA, Name = "A" },
                    new Organization { ItemId = OrgB, Name = "B" }
                ]);

            var result = await Create().GetMyOrganizationAsync();

            result.IsSuccess.Should().BeTrue();
            result.Organizations.Select(o => o.ItemId).Should().Equal(OrgA, OrgB);
        }
    }
}
