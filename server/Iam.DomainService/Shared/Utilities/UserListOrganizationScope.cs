namespace Iam.DomainService.Utilities
{
    /// <summary>
    /// What the user list is allowed to return for one caller.
    /// </summary>
    public enum UserListScopeKind
    {
        /// <summary>The caller names no organization, so no user may be returned.</summary>
        Denied,

        /// <summary>Every organization in the tenant; the query carries no organization clause.</summary>
        AllOrganizations,

        /// <summary>The union of the organizations in <see cref="UserListScope.OrganizationIds"/>.</summary>
        Organizations
    }

    /// <param name="Kind">Which of the three outcomes applies.</param>
    /// <param name="OrganizationIds">
    /// The organizations to match, non-empty exactly when <paramref name="Kind"/> is
    /// <see cref="UserListScopeKind.Organizations"/>.
    /// </param>
    public sealed record UserListScope(UserListScopeKind Kind, IReadOnlyList<string> OrganizationIds);

    /// <summary>
    /// The one place that decides which organizations a user-list query may read.
    /// </summary>
    /// <remarks>
    /// Extracted so the decision is a pure function of the token's organization and the requested
    /// one, testable without a database, and impossible to express differently in two places.
    /// <para>
    /// It deliberately does NOT collapse a missing organization to "default". That collapse is the
    /// bug this type exists to prevent: it makes "the caller named no organization" indistinguishable
    /// from "the caller named the default organization", which turns the deny case into a
    /// tenant-wide read (fail-open) and makes an explicit request for the default organization
    /// silently mean "every organization". Both comparisons here are made on the raw values.
    /// </para>
    /// <para>
    /// A "default" organization is the tenant-wide scope, matching the privilege the mutation side
    /// already grants that context, and is the only caller allowed to choose organizations from the
    /// request. Every other caller is pinned to its own organization and the requested list is
    /// discarded rather than rejected, so a payload can never widen what a token authorises.
    /// </para>
    /// </remarks>
    public static class UserListOrganizationScope
    {
        private static readonly UserListScope DeniedScope = new(UserListScopeKind.Denied, []);
        private static readonly UserListScope AllOrganizationsScope = new(UserListScopeKind.AllOrganizations, []);

        public static UserListScope Resolve(string? tokenOrganizationId, IEnumerable<string>? requestedOrganizationIds)
        {
            // Rule 4 first: a token with no organization is denied outright, and must be tested
            // before the "default" comparison so an empty value can never fall through to it.
            if (string.IsNullOrWhiteSpace(tokenOrganizationId))
            {
                return DeniedScope;
            }

            // Rule 3: any organization other than "default" pins the caller to itself. The requested
            // list is dropped here, not validated, so an unauthorised id is ignored rather than
            // echoed back as an error.
            if (!string.Equals(tokenOrganizationId, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal))
            {
                return new UserListScope(UserListScopeKind.Organizations, [tokenOrganizationId]);
            }

            var requested = Sanitize(requestedOrganizationIds);

            // Rules 1 and 2: the tenant-wide caller gets everything unless it narrows the list itself.
            return requested.Count == 0
                ? AllOrganizationsScope
                : new UserListScope(UserListScopeKind.Organizations, requested);
        }

        /// <summary>
        /// Drop blank entries and duplicates, preserving the caller's order. Ids are otherwise passed
        /// through untouched: they are matched verbatim against the stored organization ids, so
        /// trimming or case-folding here would invent matches the database would not make.
        /// </summary>
        private static List<string> Sanitize(IEnumerable<string>? organizationIds)
        {
            if (organizationIds is null)
            {
                return [];
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();

            foreach (var organizationId in organizationIds)
            {
                if (!string.IsNullOrWhiteSpace(organizationId) && seen.Add(organizationId))
                {
                    result.Add(organizationId);
                }
            }

            return result;
        }
    }
}
