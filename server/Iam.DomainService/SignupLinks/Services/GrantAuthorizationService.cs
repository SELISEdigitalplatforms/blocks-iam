using Blocks.Genesis;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.ResponseModel;

namespace Iam.DomainService.SignupLinks;

public class GrantAuthorizationService : IGrantAuthorizationService
{
    private readonly IResourceRepository _resourceRepository;

    public GrantAuthorizationService(IResourceRepository resourceRepository)
    {
        _resourceRepository = resourceRepository;
    }

    public async Task<GetAssignableRolesResponse> GetAssignableRolesAsync()
    {
        var bc = BlocksContext.GetContext();
        var userRoles = bc?.Roles ?? [];

        var (roles, _) = await _resourceRepository.GetRolesAsync(new GetRolesRequest
        {
            PageSize = 1000
        });

        var referencedAncestorSlugs = roles
            .SelectMany(x => x.AncestorRoleSlugs ?? new List<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // User roles that can create descendants
        var creatableRoles = roles
            .Where(x =>
                userRoles.Contains(x.Slug, StringComparer.OrdinalIgnoreCase)
                && x.CanCreateOwn)
            .Select(x => x.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hierarchy = new List<AssignableRole>();
        var standalone = new List<AssignableRole>();

        foreach (var role in roles)
        {
            var isStandalone =
                !role.CanCreateOwn &&
                string.IsNullOrWhiteSpace(role.ParentRoleSlug) &&
                !role.AncestorRoleSlugs.Any() &&
                !referencedAncestorSlugs.Contains(role.Slug);

            if (isStandalone)
            {
                standalone.Add(new AssignableRole
                {
                    Slug = role.Slug,
                    Name = role.Name
                });

                continue;
            }

            var isDescendantOrSelf =
                creatableRoles.Contains(role.Slug) ||
                role.AncestorRoleSlugs.Any(a =>
                    creatableRoles.Contains(a));

            if (isDescendantOrSelf)
            {
                hierarchy.Add(new AssignableRole
                {
                    Slug = role.Slug,
                    Name = role.Name
                });
            }
        }

        return new GetAssignableRolesResponse
        {
            Hierarchy = hierarchy,
            Standalone = standalone
        };
    }

    public async Task<string?> FindUngrantableRoleAsync(IEnumerable<string> roles)
    {
        var assignable = await GetAssignableRolesAsync();
        var allowed = assignable.Hierarchy
            .Concat(assignable.Standalone)
            .Select(r => r.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return roles.Where(slug => string.IsNullOrWhiteSpace(slug) || !allowed.Contains(slug))
            .Select(slug => slug ?? string.Empty)
            .FirstOrDefault();
    }

    public string? FindUngrantablePermission(IEnumerable<string> permissions)
    {
        var bc = BlocksContext.GetContext();
        var held = (bc?.Permissions ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return permissions.Where(permission => string.IsNullOrWhiteSpace(permission) || !held.Contains(permission))
            .Select(permission => permission ?? string.Empty)
            .FirstOrDefault();
    }
}
