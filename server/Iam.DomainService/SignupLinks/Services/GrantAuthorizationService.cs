using Iam.DomainService.Resources;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;

namespace Iam.DomainService.SignupLinks;

/// <summary>
/// The signup-link and assignable-roles face of <see cref="IAccessPolicyService"/>. It holds no rule
/// of its own, so a link can never grant what the user endpoints would refuse.
/// </summary>
public class GrantAuthorizationService : IGrantAuthorizationService
{
    private readonly IAccessPolicyService _accessPolicy;

    public GrantAuthorizationService(IResourceRepository resourceRepository, IAccessPolicyService? accessPolicy = null)
    {
        _accessPolicy = accessPolicy ?? new AccessPolicyService(resourceRepository);
    }

    public async Task<GetAssignableRolesResponse> GetAssignableRolesAsync()
    {
        var caller = await _accessPolicy.ResolveCallerAsync();
        var grantable = await _accessPolicy.GetGrantableRolesAsync(caller);

        var response = new GetAssignableRolesResponse();

        foreach (var role in grantable)
        {
            var item = new AssignableRole
            {
                Slug = role.Slug,
                Name = role.Name,
                ParentRoleSlug = role.ParentRoleSlug,
                CanCreateOwn = role.CanCreateOwn,
                IsTopRole = UserAccessPolicy.IsTopRole(role)
            };

            if (UserAccessPolicy.IsStandalone(role, caller.OrganizationRoles))
            {
                response.Standalone.Add(item);
            }
            else
            {
                response.Hierarchy.Add(item);
            }
        }

        return response;
    }

    public async Task<string?> FindUngrantableRoleAsync(IEnumerable<string> roles, string? organizationId = null)
    {
        var caller = await _accessPolicy.ResolveCallerAsync(organizationId);
        return await _accessPolicy.FindUngrantableRoleAsync(caller, roles);
    }

    public Task<CallerAccess> ResolveCallerAsync(string? organizationId = null) =>
        _accessPolicy.ResolveCallerAsync(organizationId);

    public async Task<string?> FindUngrantablePermissionAsync(IEnumerable<string> permissions, string? organizationId = null)
    {
        var caller = await _accessPolicy.ResolveCallerAsync(organizationId);
        return await _accessPolicy.FindUngrantablePermissionAsync(caller, permissions);
    }
}
