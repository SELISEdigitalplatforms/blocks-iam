using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Moq;

namespace XUnitTest.TestSupport
{
    /// <summary>
    /// Seeds the role and permission reads the access policy makes, for fixtures whose subject is
    /// something else. Every seeded role is standalone and carries no permission, so any caller may
    /// grant it: tests that exercise the grant rules themselves set up their own hierarchy instead.
    /// </summary>
    public static class AccessPolicyMocks
    {
        public static readonly string[] CommonRoles =
        [
            "admin", "member", "viewer", "editor", "auditor", "manager", "user",
            "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9", "r10"
        ];

        /// <summary>Permissions a seeded caller holds directly, so it may grant them.</summary>
        public static readonly string[] CommonPermissions =
        [
            "read", "write", "p", "p1", "p2", "p3", "p4", "p5", "p6", "1", "2", "3", "4", "5", "6"
        ];

        public static void SeedOrganization(Mock<IResourceRepository> repository, params string[] extraRoles)
        {
            var roles = CommonRoles.Concat(extraRoles).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(slug => new Role { ItemId = $"role-{slug}", Slug = slug, Name = slug, CanCreateOwn = false, AncestorRoleSlugs = [] })
                .ToList();

            repository.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
                .ReturnsAsync((roles.AsQueryable(), (long)roles.Count));
            repository.Setup(r => r.GetRolesByOrgAsync(It.IsAny<string>())).ReturnsAsync(roles);
            repository.Setup(r => r.GetActivePermissionsByResourcesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
                .ReturnsAsync((IEnumerable<string> keys, string _) => keys.Select(k => new Permission { Resource = k }).ToList());
        }
    }
}
