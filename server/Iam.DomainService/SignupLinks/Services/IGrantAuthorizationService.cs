using Iam.DomainService.Resources.ResponseModel;

namespace Iam.DomainService.SignupLinks;

public interface IGrantAuthorizationService
{
    /// <summary>
    /// Same assignable-role classification previously owned by
    /// <c>ResourceQueryService.GetAssignableRolesAsync</c> — hierarchy + standalone.
    /// </summary>
    Task<GetAssignableRolesResponse> GetAssignableRolesAsync();

    /// <summary>
    /// Returns the first requested role the caller cannot grant, or null if all are allowed.
    /// </summary>
    Task<string?> FindUngrantableRoleAsync(IEnumerable<string> roles);

    /// <summary>
    /// Returns the first requested permission the caller does not hold, or null if all are allowed.
    /// </summary>
    string? FindUngrantablePermission(IEnumerable<string> permissions);
}
