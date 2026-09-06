namespace Iam.DomainService.Utilities
{
    /// <summary>Which organizations a caller may read or write through the organization endpoints.</summary>
    public enum OrganizationAccessScopeKind
    {
        /// <summary>The caller's token names no organization, so nothing may be read or written.</summary>
        Denied,

        /// <summary>Every organization in the tenant. Only the tenant-wide caller gets this.</summary>
        AllOrganizations,

        /// <summary>Exactly <see cref="OrganizationAccessScope.OrganizationId"/>, and nothing else.</summary>
        Organization
    }

    /// <param name="Kind">Which of the three outcomes applies.</param>
    /// <param name="OrganizationId">
    /// The single organization the caller is pinned to, non-empty exactly when <paramref name="Kind"/>
    /// is <see cref="OrganizationAccessScopeKind.Organization"/>.
    /// </param>
    public sealed record OrganizationAccessScope(OrganizationAccessScopeKind Kind, string OrganizationId);

    /// <summary>
    /// The one place that decides which organizations the organization endpoints may reach.
    /// </summary>
    /// <remarks>
    /// The organization endpoints were the last family to read organization documents with no
    /// organization scope at all: the permission claim is minted per organization
    /// (<c>user.Permissions[org_id]</c>), so it says "you may administer organizations <i>here</i>",
    /// but <c>GetOrganizationById</c> filtered on the id alone and the list used an empty filter.
    /// A caller granted the permission in one organization could therefore read, and write, every
    /// other organization in the tenant. This type closes that gap the same way
    /// <see cref="UserListOrganizationScope"/> and <see cref="ResourceWriteOrganizationScope"/>
    /// already close it for user lists and role/permission writes.
    /// <para>
    /// Scope is not permission. This answers only "which organizations", never "may you at all" --
    /// the <c>ProtectedEndPoint</c> attribute still gates every one of these endpoints. Reach
    /// without the permission is nothing.
    /// </para>
    /// <para>
    /// Deny is tested first and is never reachable by fallback. A blank claim must not collapse to
    /// <see cref="IdpConstants.DefaultOrganizationId"/>, because "default" is simultaneously "this
    /// project has no organizations" and "tenant-wide root privilege" -- so an unknown answer would
    /// silently become the most privileged one. <see cref="IdpConstants.NoOrganizationId"/> is
    /// denied explicitly rather than treated as an organization id that happens to match nothing.
    /// </para>
    /// </remarks>
    public static class OrganizationAccessScopeResolver
    {
        private static readonly OrganizationAccessScope DeniedScope =
            new(OrganizationAccessScopeKind.Denied, string.Empty);

        private static readonly OrganizationAccessScope AllOrganizationsScope =
            new(OrganizationAccessScopeKind.AllOrganizations, string.Empty);

        /// <summary>
        /// Total: every input yields a scope. Never throws.
        /// </summary>
        /// <param name="tokenOrganizationId">
        /// The organization claim from the caller's token, normally
        /// <c>BlocksContext.GetContext()?.OrganizationId</c>. It is the authority: no request
        /// payload or route value may widen it.
        /// </param>
        public static OrganizationAccessScope Resolve(string? tokenOrganizationId)
        {
            // Tested before the "default" comparison so an empty value can never fall through and
            // be read as the tenant-wide scope.
            if (string.IsNullOrWhiteSpace(tokenOrganizationId))
            {
                return DeniedScope;
            }

            var tokenOrganization = tokenOrganizationId.Trim();

            // An explicit "belongs to nothing". A member of no organization has no organization to
            // read, and must not be pinned to "no-org" as though it were a real id.
            if (string.Equals(tokenOrganization, IdpConstants.NoOrganizationId, StringComparison.Ordinal))
            {
                return DeniedScope;
            }

            if (string.Equals(tokenOrganization, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal))
            {
                return AllOrganizationsScope;
            }

            return new OrganizationAccessScope(OrganizationAccessScopeKind.Organization, tokenOrganization);
        }

        /// <summary>
        /// True when <paramref name="organizationId"/> is within <paramref name="scope"/>.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="ResourceWriteOrganizationScope"/>, which discards an unauthorised id and
        /// pins the caller to its own, the organization endpoints address their target by route id.
        /// Silently retargeting is safe for a payload field that narrows a query and dangerous for a
        /// route id that names the write target: <c>POST organizations/{someone-elses-id}</c> would
        /// rewrite the caller's OWN organization with that payload and answer 200. So the caller is
        /// tested against the scope here, and a miss is reported by the caller as "not found" -- which
        /// still leaks nothing about whether the organization exists.
        /// </remarks>
        public static bool Allows(this OrganizationAccessScope scope, string? organizationId)
        {
            ArgumentNullException.ThrowIfNull(scope);

            return scope.Kind switch
            {
                OrganizationAccessScopeKind.AllOrganizations => true,
                OrganizationAccessScopeKind.Organization =>
                    string.Equals(scope.OrganizationId, organizationId?.Trim(), StringComparison.Ordinal),
                _ => false
            };
        }
    }
}
