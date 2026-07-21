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
    }
}
