using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.SignupLinks;
using Moq;

namespace XUnitTest.IamTests.SignupLinks;

public class GrantAuthorizationServiceTests : IDisposable
{
    private readonly Mock<IResourceRepository> _repo = new();

    private GrantAuthorizationService Sut() => new(_repo.Object);

    private static void SetContext(
        string orgId = "default",
        IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null)
    {
        BlocksContext.IsTestMode = true;
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: "tenant-1", roles: roles, userId: "actor-1", impersonated: false,
            isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
            permissions: permissions, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
            userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
            originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
    }

    public void Dispose()
    {
        BlocksContext.SetContext(null);
        BlocksContext.IsTestMode = false;
    }

    [Fact]
    public async Task GetAssignableRoles_ClassifiesHierarchyAndStandalone()
    {
        SetContext(roles: new List<string> { "manager" });
        var roles = new List<Role>
        {
            new() { Slug = "manager", Name = "Manager", CanCreateOwn = true, ParentRoleSlug = null, AncestorRoleSlugs = new() },
            new() { Slug = "team-lead", Name = "Team Lead", CanCreateOwn = true, ParentRoleSlug = "manager", AncestorRoleSlugs = new() { "manager" } },
            new() { Slug = "admin", Name = "Admin", CanCreateOwn = true, ParentRoleSlug = null, AncestorRoleSlugs = new() },
            new() { Slug = "guest", Name = "Guest", CanCreateOwn = false, ParentRoleSlug = null, AncestorRoleSlugs = new() },
        }.AsQueryable();
        _repo.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
            .ReturnsAsync((roles, 4L));

        var result = await Sut().GetAssignableRolesAsync();

        result.Hierarchy.Select(x => x.Slug).Should().BeEquivalentTo("manager", "team-lead");
        result.Standalone.Select(x => x.Slug).Should().BeEquivalentTo("guest");
        result.Hierarchy.Select(x => x.Slug).Should().NotContain("admin");
    }

    [Fact]
    public async Task FindUngrantableRole_ReturnsFirstDenied()
    {
        SetContext(roles: new List<string> { "site-manager" });
        var roles = new List<Role>
        {
            new() { Slug = "site-manager", Name = "SM", CanCreateOwn = true, AncestorRoleSlugs = new() },
            new() { Slug = "site-viewer", Name = "SV", CanCreateOwn = false, ParentRoleSlug = "site-manager", AncestorRoleSlugs = new() { "site-manager" } },
            new() { Slug = "tenant-admin", Name = "TA", CanCreateOwn = true, AncestorRoleSlugs = new() },
            new() { Slug = "contractor", Name = "C", CanCreateOwn = false, AncestorRoleSlugs = new() },
        }.AsQueryable();
        _repo.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
            .ReturnsAsync((roles, 4L));

        (await Sut().FindUngrantableRoleAsync(["site-viewer"])).Should().BeNull();
        (await Sut().FindUngrantableRoleAsync(["contractor"])).Should().BeNull();
        (await Sut().FindUngrantableRoleAsync(["tenant-admin"])).Should().Be("tenant-admin");
    }

    [Fact]
    public async Task FindUngrantablePermission_RequiresHeldPermission()
    {
        SetContext(orgId: "org-a", permissions: new List<string> { "read:project" });
        _repo.Setup(r => r.GetActivePermissionsByResourcesAsync(It.IsAny<IEnumerable<string>>(), "org-a"))
            .ReturnsAsync((IEnumerable<string> keys, string _) => keys.Select(k => new Permission { Resource = k }).ToList());

        (await Sut().FindUngrantablePermissionAsync(["read:project"])).Should().BeNull();
        (await Sut().FindUngrantablePermissionAsync(["write:project"])).Should().Be("write:project");
    }
}
