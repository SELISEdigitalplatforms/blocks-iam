using Authentication.DomainService.Shared;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Utilities;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.Utilities
{
    /// <summary>
    /// Resolves which organization a user should be signed into.
    ///
    /// Selection rules (preserved from the original AuthorizationFlowService private helper):
    ///   1. LastUsedOrganizationId, if it is still in OrganizationIds.
    ///   2. The "default" organization, if it is in OrganizationIds.
    ///   3. The first organization in OrganizationIds, falling back to the first
    ///      role key, falling back to the first permission key.
    /// </summary>
    public static class OrganizationAccessResolver
    {
        public static string? ResolveEffectiveOrganizationId(User user)
        {
            if (!string.IsNullOrWhiteSpace(user.LastUsedOrganizationId)
                && user.OrganizationIds.Contains(user.LastUsedOrganizationId))
            {
                return user.LastUsedOrganizationId;
            }

            if (user.OrganizationIds.Contains(IdpConstants.DefaultOrganizationId))
            {
                return IdpConstants.DefaultOrganizationId;
            }

            return user.OrganizationIds.FirstOrDefault()
                ?? user.Roles.Keys.FirstOrDefault()
                ?? user.Permissions.Keys.FirstOrDefault();
        }

        /// <summary>
        /// The organization an interactive sign-in should be scoped to. Every credential-based leg
        /// (password, social, and the mfa second leg that completes either of them) must agree on
        /// this value: it becomes the organization claim AND the key
        /// <see cref="OAuth.AuthorizationClaimsResolver"/> reads roles and permissions out of, so a
        /// leg that skips it mints a token scoped to "default" with no roles for an org-scoped user.
        ///
        /// Membership is the three-way test — OrganizationIds, Roles keys, or Permissions keys —
        /// because an organization can be granted through a role or permission assignment without
        /// ever being written to OrganizationIds. <see cref="ResolveEffectiveOrganizationId"/>
        /// deliberately keeps the stricter OrganizationIds-only test for the OIDC authorize
        /// endpoint and is not interchangeable with this one.
        ///
        /// A caller-supplied organization is honoured only when the user actually belongs to it, so
        /// this is safe to apply to request-controlled input.
        /// </summary>
        public static string ResolveSignInOrganizationId(User user, string? requestedOrganizationId)
        {
            if (HasOrganizationAccess(user, requestedOrganizationId))
            {
                return requestedOrganizationId!;
            }

            if (HasOrganizationAccess(user, user.LastUsedOrganizationId))
            {
                return user.LastUsedOrganizationId!;
            }

            if (HasOrganizationAccess(user, IdpConstants.DefaultOrganizationId))
            {
                return IdpConstants.DefaultOrganizationId;
            }

            return user.OrganizationIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                ?? user.Roles.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? user.Permissions.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? IdpConstants.DefaultOrganizationId;
        }

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
