namespace Iam.DomainService.Utilities
{
    /// <summary>Which organization a role or permission write is allowed to target.</summary>
    public enum ResourceWriteScopeKind
    {
        /// <summary>The caller's token names no organization, so nothing may be written.</summary>
        Denied,

        /// <summary>The write targets <see cref="ResourceWriteScope.OrganizationId"/>.</summary>
        Organization
    }

    /// <param name="Kind">Which of the two outcomes applies.</param>
    /// <param name="OrganizationId">
    /// The organization to write to, empty exactly when <paramref name="Kind"/> is
    /// <see cref="ResourceWriteScopeKind.Denied"/>.
    /// </param>
    public sealed record ResourceWriteScope(ResourceWriteScopeKind Kind, string OrganizationId);

    /// <summary>
    /// The one place that decides which organization a role or permission WRITE may target.
    /// </summary>
    /// <remarks>
    /// The write-side counterpart of <see cref="UserListOrganizationScope"/>, and it applies the
    /// same rule for the same reason: the signed organization claim is the authority, and a payload
    /// can never widen what a token authorises. A non-default caller is pinned to its own
    /// organization and any requested id is <b>discarded rather than rejected</b>, so a client that
    /// redundantly sends its own organization id keeps working and one that sends someone else's
    /// gets its own instead of an error.
    /// <para>
    /// It deliberately does NOT collapse a missing organization to "default", which is what
    /// <c>ResolveOrganizationId</c> does. That collapse is fail-open on a write path: a caller with
    /// no organization claim would be handed the tenant-wide scope, which is the most privileged
    /// one there is. Here it denies instead.
    /// </para>
    /// </remarks>
    public static class ResourceWriteOrganizationScope
    {
        private static readonly ResourceWriteScope DeniedScope =
            new(ResourceWriteScopeKind.Denied, string.Empty);

        public static ResourceWriteScope Resolve(string? tokenOrganizationId, string? requestedOrganizationId)
        {
            // Tested first so an empty value can never fall through to the "default" comparison
            // below and be read as the tenant-wide scope.
            if (string.IsNullOrWhiteSpace(tokenOrganizationId))
            {
                return DeniedScope;
            }

            var tokenOrganization = tokenOrganizationId.Trim();

            // Any organization other than "default" pins the caller to itself. The requested id is
            // dropped here, not validated, so an unauthorised one is ignored rather than echoed
            // back -- the caller learns nothing about whether it exists.
            if (!string.Equals(tokenOrganization, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal))
            {
                return new ResourceWriteScope(ResourceWriteScopeKind.Organization, tokenOrganization);
            }

            // The tenant-wide caller is the only one allowed to choose the target from the request.
            return new ResourceWriteScope(
                ResourceWriteScopeKind.Organization,
                string.IsNullOrWhiteSpace(requestedOrganizationId)
                    ? IdpConstants.DefaultOrganizationId
                    : requestedOrganizationId.Trim());
        }
    }
}
