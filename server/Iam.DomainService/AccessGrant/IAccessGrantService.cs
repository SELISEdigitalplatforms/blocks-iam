using Iam.DomainService.Resources.ResponseModel;

namespace Iam.DomainService.AccessGrant
{
    /// <summary>
    /// access grant -- position in the role tree decides power -- in one place: who the system user
    /// is, the caller's <see cref="GrantScope"/>, what a refusal does, the
    /// hidden-power warning (R11) and ending sessions after a removal (R12). The rules themselves
    /// are <see cref="AccessGrantRules"/>.
    /// </summary>
    public interface IAccessGrantService
    {
        /// <summary>The current caller from <c>BlocksContext</c>; never null.</summary>
        GrantCaller CurrentCaller();

        /// <summary>
        /// True when the current call comes from the Blocks OS console: an impersonated token (OS acting
        /// inside a project) or a root-tenant token (OS on its own tenant). A project's own token, as an
        /// app such as Construct holds, is not. Both signals are signed token claims.
        /// </summary>
        bool IsConsoleCaller();

        /// <summary>The current caller's power in its token organization. No reads for the system user.</summary>
        Task<GrantScope> GetScopeAsync();

        /// <summary>A given caller's power -- the bulk consumer, which runs after the request is gone.</summary>
        Task<GrantScope> GetScopeAsync(GrantCaller caller);

        /// <summary>
        /// Applies a check's result. Passes, and the system user, return true silently.
        /// A denial is logged and returns false.
        /// </summary>
        bool Authorize(GrantScope scope, GrantDenial? denial);

        /// <summary>R6 for the current caller, then refuses a denial. Needs no reads: only who the caller is matters.</summary>
        bool AuthorizeSystemOnly(string target);

        /// <summary>Loads the current caller's scope, runs <paramref name="check"/>, and applies the mode.</summary>
        Task<bool> AuthorizeCurrentCallerAsync(Func<GrantScope, GrantDenial?> check);

        /// <summary>
        /// R3 for a path that acts on another user by id alone (session revoke). A user that does
        /// not exist passes: there is nothing to protect, and the path answers it as it always has.
        /// </summary>
        Task<bool> AuthorizeManageUserAsync(string userId);

        /// <summary>
        /// R1/R2 on everything granted plus the R11 warning for the roles, then the mode. The usual
        /// call for a path that hands out roles and permissions.
        /// </summary>
        Task<bool> AuthorizeGrantAsync(GrantScope scope, IEnumerable<string>? roles, IEnumerable<string>? permissions, string? rule = null);

        /// <summary>
        /// R11, warn only: logs <c>AccessGrant.RoleCarriesPolicyPower</c> for each role
        /// a data-access policy names that the caller neither holds nor manages. Never throws.
        /// </summary>
        Task WarnOnPolicyPowerAsync(GrantScope scope, IEnumerable<string>? roles);

        /// <summary>
        /// Users holding <paramref name="slug"/> in the scope's organization, read before a removal
        /// so R12 knows whose sessions to end. Empty for the system user, to whom R12 does not apply.
        /// </summary>
        Task<IReadOnlyList<string>> GetRoleHoldersAsync(GrantScope scope, string slug);

        /// <summary>
        /// R12: a non-system caller removed at least one role or permission from these users, so
        /// every session they hold ends. Never throws.
        /// </summary>
        Task EndSessionsAfterRemovalAsync(GrantScope scope, IEnumerable<string> userIds, string reason);

        /// <summary>
        /// <c>GET roles/assignable</c>: the roles that pass R1 for a non-system caller; for the
        /// system user, unchanged from before (<see cref="SignupLinks.IGrantAuthorizationService"/>).
        /// </summary>
        Task<GetAssignableRolesResponse> GetAssignableRolesAsync();

        /// <summary><c>GET iam/grant-scope</c> for the token organization.</summary>
        Task<GrantScopeResponse> GetGrantScopeAsync();
    }
}
