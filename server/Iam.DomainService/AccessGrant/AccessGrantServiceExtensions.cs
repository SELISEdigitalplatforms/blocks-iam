namespace Iam.DomainService.AccessGrant
{
    /// <summary>
    /// The calls the mutation services make, tolerant of a service that was not wired. DI always
    /// wires it; the long-lived test fixtures that build those services by hand do not, and for
    /// them nothing changes. Kept here so every call site reads the same and none re-implements it.
    /// </summary>
    public static class AccessGrantServiceExtensions
    {
        /// <summary>The current caller's scope, or null when the service is not wired.</summary>
        public static async Task<GrantScope?> GetScopeIfWiredAsync(this IAccessGrantService? service) =>
            service is null ? null : await service.GetScopeAsync().ConfigureAwait(false);

        /// <summary>Runs <paramref name="check"/> on <paramref name="scope"/> and applies the mode; true when allowed.</summary>
        public static bool AuthorizeScopeIfWired(
            this IAccessGrantService? service,
            GrantScope? scope,
            Func<GrantScope, GrantDenial?> check)
        {
            ArgumentNullException.ThrowIfNull(check);
            return service is null || scope is null || scope.IsSystem || service.Authorize(scope, check(scope));
        }

        /// <summary>Loads the current caller's scope, runs <paramref name="check"/> and applies the mode.</summary>
        public static async Task<bool> AuthorizeCurrentCallerIfWiredAsync(
            this IAccessGrantService? service,
            Func<GrantScope, GrantDenial?> check) =>
            service is null || await service.AuthorizeCurrentCallerAsync(check).ConfigureAwait(false);

        /// <summary>R1/R2 plus the R11 warning on a grant, then the mode.</summary>
        public static async Task<bool> AuthorizeGrantIfWiredAsync(
            this IAccessGrantService? service,
            GrantScope? scope,
            IEnumerable<string>? roles,
            IEnumerable<string>? permissions,
            string? rule = null) =>
            service is null
            || scope is null
            || await service.AuthorizeGrantAsync(scope, roles, permissions, rule).ConfigureAwait(false);

        /// <summary>R6 for the current caller, then the mode.</summary>
        public static bool AuthorizeSystemOnlyIfWired(this IAccessGrantService? service, string target) =>
            service is null || service.AuthorizeSystemOnly(target);

        /// <summary>R11 for roles about to be granted.</summary>
        public static async Task WarnOnPolicyPowerIfWiredAsync(
            this IAccessGrantService? service,
            GrantScope? scope,
            IEnumerable<string>? roles)
        {
            if (service is not null && scope is not null)
            {
                await service.WarnOnPolicyPowerAsync(scope, roles).ConfigureAwait(false);
            }
        }

        /// <summary>Holders of a role, read before a removal so R12 can end their sessions.</summary>
        public static async Task<IReadOnlyList<string>> GetRoleHoldersIfWiredAsync(
            this IAccessGrantService? service,
            GrantScope? scope,
            string slug) =>
            service is null || scope is null
                ? []
                : await service.GetRoleHoldersAsync(scope, slug).ConfigureAwait(false);

        /// <summary>R12 for users who lost at least one role or permission.</summary>
        public static async Task EndSessionsAfterRemovalIfWiredAsync(
            this IAccessGrantService? service,
            GrantScope? scope,
            IReadOnlyCollection<string> userIds,
            string reason)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            if (service is not null && scope is not null && userIds.Count > 0)
            {
                await service.EndSessionsAfterRemovalAsync(scope, userIds, reason).ConfigureAwait(false);
            }
        }
    }
}
