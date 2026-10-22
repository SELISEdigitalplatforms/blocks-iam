using Iam.DomainService.Resources.ResponseModel;

namespace Iam.DomainService.DelegatedAdministration
{
    /// <summary>
    /// Delegated administration -- "grant only what you hold" -- in one place: who the system user
    /// is, the caller's <see cref="GrantScope"/>, what the mode does with a would-deny, the
    /// hidden-power warning (R11) and ending sessions after a removal (R12). The rules themselves
    /// are <see cref="DelegatedAdministrationRules"/>.
    /// </summary>
    public interface IDelegatedAdministrationService
    {
        DelegatedAdministrationMode Mode { get; }

        /// <summary>The current caller from <c>BlocksContext</c>; never null.</summary>
        DelegationCaller CurrentCaller();

        /// <summary>The current caller's power in its token organization. No reads for the system user.</summary>
        Task<GrantScope> GetScopeAsync();

        /// <summary>A given caller's power -- the bulk consumer, which runs after the request is gone.</summary>
        Task<GrantScope> GetScopeAsync(DelegationCaller caller);

        /// <summary>
        /// Applies the mode to a check's result. Passes, and the system user, return true silently.
        /// A denial is logged; in Audit it returns true, in Enforce false.
        /// </summary>
        bool Authorize(GrantScope scope, DelegationDenial? denial);

        /// <summary>R6 for the current caller, then the mode. Needs no reads: only who the caller is matters.</summary>
        bool AuthorizeSystemOnly(string target);

        /// <summary>Loads the current caller's scope, runs <paramref name="check"/>, and applies the mode.</summary>
        Task<bool> AuthorizeCurrentCallerAsync(Func<GrantScope, DelegationDenial?> check);

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
        /// R11, warn only: logs <c>DelegatedAdministration.RoleCarriesPolicyPower</c> for each role
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
        /// every session they hold ends (Enforce) or would end (Audit, logged). Never throws.
        /// </summary>
        Task EndSessionsAfterRemovalAsync(GrantScope scope, IEnumerable<string> userIds, string reason);

        /// <summary><c>GET roles/assignable</c>: the roles that pass R1 for the current caller.</summary>
        Task<GetAssignableRolesResponse> GetAssignableRolesAsync();

        /// <summary><c>GET iam/delegation-scope</c> for the token organization.</summary>
        Task<DelegationScopeResponse> GetDelegationScopeAsync();
    }
}
