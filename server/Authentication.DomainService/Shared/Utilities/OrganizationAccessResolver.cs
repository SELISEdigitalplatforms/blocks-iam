using Authentication.DomainService.Shared;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Utilities;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.Utilities
{
    /// <summary>
    /// Resolves which organization a user should be signed into.
    ///
    /// Both methods are thin adapters over <see cref="OrganizationScopeResolver"/>, which owns the
    /// rules and is the single place they live. This type contributes the membership test
    /// (<see cref="HasOrganizationAccess"/>) and keeps the call shapes the sign-in legs already use.
    ///
    /// Selection rules (see <see cref="OrganizationScopeResolver.Resolve"/> for the authoritative order):
    ///   1. The requested organization, if the user belongs to it and it is not a scope sentinel.
    ///   2. LastUsedOrganizationId, if the user still belongs to it.
    ///   3. The first organization id, falling back to the first role key, then the first
    ///      permission key.
    ///   4. Otherwise "no-org" -- never "default", which under multi-org is the tenant-wide scope
    ///      and must be granted explicitly, never reached by a fallback.
    /// </summary>
    public static class OrganizationAccessResolver
    {
        /// <summary>
        /// The organization an OIDC authorize / device-approval leg should record. Unlike the
        /// sign-in legs below, the value this returns is stored (on the authorization code or the
        /// device approval) and becomes the claim without being re-validated at mint time, so the
        /// caller must pass the real <paramref name="isMultiOrgEnabled"/> rather than relying on a
        /// later correction.
        /// <para>
        /// Returns a non-null, non-empty scope value. It used to return <c>null</c> when the user
        /// belonged to nothing, which made the organization claim disappear from the token
        /// altogether -- and a blank organization is read as <c>"default"</c> downstream.
        /// </para>
        /// </summary>
        public static string ResolveEffectiveOrganizationId(User user, bool isMultiOrgEnabled) =>
            Resolve(user, requestedOrganizationId: null, isMultiOrgEnabled).ClaimValue;

        /// <summary>
        /// The organization an interactive sign-in should be scoped to. Every credential-based leg
        /// (password, social, and the mfa second leg that completes either of them) must agree on
        /// this value: it becomes the organization claim AND the key
        /// <see cref="OAuth.AuthorizationClaimsResolver"/> reads roles and permissions out of, so a
        /// leg that skips it mints a token with no roles for an org-scoped user.
        ///
        /// A caller-supplied organization is honoured only when the user actually belongs to it, so
        /// this is safe to apply to request-controlled input.
        /// </summary>
        /// <param name="isMultiOrgEnabled">
        /// Optional here, and defaulted to <c>true</c>, because these legs only <b>pre-fill</b>
        /// <c>TokenRequest.OrganizationId</c>: the authoritative decision is taken again inside
        /// <see cref="OAuth.JwtAccessTokenProvider.GetJwtAccessToken"/>, which reads the tenant's
        /// real mode. A single-organization tenant therefore still mints <c>"default"</c> even if
        /// this pre-fill said otherwise.
        /// </param>
        public static string ResolveSignInOrganizationId(
            User user,
            string? requestedOrganizationId,
            bool isMultiOrgEnabled = true) =>
            Resolve(user, requestedOrganizationId, isMultiOrgEnabled).ClaimValue;

        /// <summary>
        /// The full scope decision, when the caller needs the <see cref="OrganizationScopeKind"/>
        /// and not just the claim value.
        /// </summary>
        public static OrganizationScope Resolve(User user, string? requestedOrganizationId, bool isMultiOrgEnabled) =>
            OrganizationScopeResolver.Resolve(isMultiOrgEnabled, user, requestedOrganizationId, HasOrganizationAccess);

        /// <summary>
        /// Membership is a three-way test -- OrganizationIds, Roles keys, or Permissions keys --
        /// because an organization can be granted through a role or permission assignment without
        /// ever being written to <see cref="User.OrganizationIds"/>.
        /// </summary>
        public static bool HasOrganizationAccess(User user, string? organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return false;
            }

            return user.OrganizationIds.Contains(organizationId)
                || user.Roles.ContainsKey(organizationId)
                || user.Permissions.ContainsKey(organizationId);
        }
    }
}
