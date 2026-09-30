using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Utilities;

namespace Iam.DomainService.SignupLinks;

public interface IGrantAuthorizationService
{
    /// <summary>
    /// The roles the caller may grant in its organization, split into those inside the hierarchy
    /// and the standalone ones.
    /// </summary>
    Task<GetAssignableRolesResponse> GetAssignableRolesAsync();

    /// <summary>
    /// Returns the first requested role the caller cannot grant in <paramref name="organizationId"/>
    /// (the caller's own when blank), or null if all are allowed.
    /// </summary>
    Task<string?> FindUngrantableRoleAsync(IEnumerable<string> roles, string? organizationId = null);

    /// <summary>
    /// Returns the first requested permission the caller cannot grant in
    /// <paramref name="organizationId"/> (the caller's own when blank), or null if all are allowed.
    /// </summary>
    Task<string?> FindUngrantablePermissionAsync(IEnumerable<string> permissions, string? organizationId = null);

    /// <summary>The caller as the access rules see it, for paths that scope by it.</summary>
    Task<CallerAccess> ResolveCallerAsync(string? organizationId = null);
}
