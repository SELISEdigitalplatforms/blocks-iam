using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Utilities;

namespace Iam.DomainService.Services
{
    /// <summary>
    /// Loads what <see cref="UserAccessPolicy"/> needs about the current caller, and answers the grant
    /// questions against the database. Every user and role write path asks here, so there is one
    /// definition of "who may do this" rather than one per endpoint.
    /// </summary>
    public interface IAccessPolicyService
    {
        /// <summary>
        /// Resolves the caller for <paramref name="targetOrganizationId"/>. The token's organization is
        /// the authority: a caller bound to one organization asking about another is
        /// <see cref="CallerKind.Denied"/>, and only a supreme caller may name the target itself. A
        /// blank target means "the caller's own organization".
        /// </summary>
        Task<CallerAccess> ResolveCallerAsync(string? targetOrganizationId = null);

        /// <summary>First role the caller may not grant or take away, or null.</summary>
        Task<string?> FindUngrantableRoleAsync(CallerAccess caller, IEnumerable<string>? roles);

        /// <summary>First permission the caller may not grant or take away, or null.</summary>
        Task<string?> FindUngrantablePermissionAsync(CallerAccess caller, IEnumerable<string>? permissions);

        /// <summary>Standalone roles the caller may grant, keyed for the assignable-roles picker.</summary>
        Task<IReadOnlyList<Role>> GetGrantableRolesAsync(CallerAccess caller);
    }

    public sealed class AccessPolicyService : IAccessPolicyService
    {
        private readonly IResourceRepository _resourceRepository;

        public AccessPolicyService(IResourceRepository resourceRepository)
        {
            _resourceRepository = resourceRepository;
        }

        public async Task<CallerAccess> ResolveCallerAsync(string? targetOrganizationId = null)
        {
            var context = BlocksContext.GetContext();
            var target = string.IsNullOrWhiteSpace(targetOrganizationId) ? null : targetOrganizationId.Trim();

            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            var isMultiOrg = tenantConfig?.IsMultiOrgEnabled ?? false;

            // No identity at all: the anonymous and queue flows. They carry their organization from
            // trusted configuration, so there is nothing to compare against.
            if (context is null || !context.IsAuthenticated)
            {
                var systemOrg = target ?? IdpConstants.DefaultOrganizationId;
                return await BuildAsync(CallerKind.System, systemOrg, context, isMultiOrg, loadPermissions: false);
            }

            // A project owner impersonating into the tenant. Their authority comes from their own
            // tenant (Genesis checks it there), and their roles do not exist here, so the hierarchy
            // cannot bind them.
            if (context.Impersonated)
            {
                return await BuildAsync(CallerKind.Supreme, target ?? ResolveOwnOrganization(context.OrganizationId), context, isMultiOrg, loadPermissions: false);
            }

            var tokenOrg = context.OrganizationId?.Trim();

            // Deny is decided before "default" is compared, so a blank claim can never be read as the
            // tenant-wide scope.
            if (string.IsNullOrWhiteSpace(tokenOrg)
                || string.Equals(tokenOrg, IdpConstants.NoOrganizationId, StringComparison.Ordinal))
            {
                return CallerAccess.Denied;
            }

            // "default" is above every organization only when there are organizations. In a
            // single-organization tenant everyone's token says "default", so treating it as supreme
            // there would switch every rule off for everyone.
            if (isMultiOrg && string.Equals(tokenOrg, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal))
            {
                return await BuildAsync(CallerKind.Supreme, target ?? tokenOrg, context, isMultiOrg, loadPermissions: false);
            }

            if (target is not null && !string.Equals(target, tokenOrg, StringComparison.Ordinal))
            {
                return CallerAccess.Denied;
            }

            var kind = string.IsNullOrWhiteSpace(context.UserId) && !string.IsNullOrWhiteSpace(context.ClientId)
                ? CallerKind.Machine
                : CallerKind.User;

            return await BuildAsync(kind, tokenOrg, context, isMultiOrg, loadPermissions: true);
        }

        private static string ResolveOwnOrganization(string? organizationId) =>
            string.IsNullOrWhiteSpace(organizationId) ? IdpConstants.DefaultOrganizationId : organizationId.Trim();

        private async Task<CallerAccess> BuildAsync(
            CallerKind kind,
            string organizationId,
            BlocksContext? context,
            bool isMultiOrg,
            bool loadPermissions)
        {
            // The same read the assignable-roles picker has always made: the organization's
            // non-archived roles, in one large page.
            var (page, _) = await _resourceRepository.GetRolesAsync(new GetRolesRequest { PageSize = 1000 }, organizationId);
            var organizationRoles = (page?.ToList() ?? [])
                .Where(r => r is not null && !r.IsArchived)
                .ToList();

            var roles = new HashSet<string>(
                (context?.Roles ?? []).Where(r => !string.IsNullOrWhiteSpace(r)),
                StringComparer.OrdinalIgnoreCase);

            var directPermissions = new HashSet<string>(
                (context?.Permissions ?? []).Where(p => !string.IsNullOrWhiteSpace(p)),
                StringComparer.OrdinalIgnoreCase);
            var effectivePermissions = new HashSet<string>(directPermissions, StringComparer.OrdinalIgnoreCase);

            if (loadPermissions && roles.Count > 0)
            {
                var granted = await _resourceRepository.GetActivePermissionsForRolesAsync(roles, organizationId) ?? [];
                foreach (var permission in granted.Where(p => !string.IsNullOrWhiteSpace(p?.Resource)))
                {
                    effectivePermissions.Add(permission.Resource);
                }
            }

            var topRoles = UserAccessPolicy.TopRoles(organizationRoles);
            var isTop = kind == CallerKind.User && topRoles.Any(r => roles.Contains(r.Slug));

            return new CallerAccess
            {
                Kind = kind,
                OrganizationId = organizationId,
                UserId = context?.UserId ?? string.Empty,
                Roles = roles,
                EffectivePermissions = effectivePermissions,
                DirectPermissions = directPermissions,
                OrganizationRoles = organizationRoles,
                IsMultiOrgEnabled = isMultiOrg,
                UsesHierarchy = UserAccessPolicy.UsesHierarchy(organizationRoles),
                IsTopRoleHolder = isTop
            };
        }

        public async Task<string?> FindUngrantableRoleAsync(CallerAccess caller, IEnumerable<string>? roles)
        {
            var requested = roles?.ToList() ?? [];
            if (requested.Count == 0)
            {
                return null;
            }

            if (caller.Kind == CallerKind.Denied)
            {
                return requested[0] ?? string.Empty;
            }

            var permissionsByRole = caller.BypassesHierarchy
                ? new Dictionary<string, IReadOnlySet<string>>()
                : await LoadPermissionsByRoleAsync(requested, caller.OrganizationId);

            return UserAccessPolicy.FindUngrantableRole(caller, requested, permissionsByRole);
        }

        public async Task<string?> FindUngrantablePermissionAsync(CallerAccess caller, IEnumerable<string>? permissions)
        {
            var requested = permissions?.ToList() ?? [];
            if (requested.Count == 0)
            {
                return null;
            }

            if (caller.Kind == CallerKind.Denied)
            {
                return requested[0] ?? string.Empty;
            }

            var existing = !caller.BypassesHierarchy
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : (await _resourceRepository.GetActivePermissionsByResourcesAsync(requested, caller.OrganizationId) ?? [])
                    .Where(p => !string.IsNullOrWhiteSpace(p?.Resource))
                    .Select(p => p.Resource)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return UserAccessPolicy.FindUngrantablePermission(caller, requested, existing);
        }

        public async Task<IReadOnlyList<Role>> GetGrantableRolesAsync(CallerAccess caller)
        {
            if (caller.Kind == CallerKind.Denied)
            {
                return [];
            }

            if (caller.BypassesHierarchy)
            {
                return caller.OrganizationRoles;
            }

            var standaloneSlugs = caller.OrganizationRoles
                .Where(r => UserAccessPolicy.IsStandalone(r, caller.OrganizationRoles))
                .Select(r => r.Slug)
                .ToList();

            var permissionsByRole = await LoadPermissionsByRoleAsync(standaloneSlugs, caller.OrganizationId);

            return caller.OrganizationRoles
                .Where(r => UserAccessPolicy.CanGrant(caller, r, permissionsByRole.GetValueOrDefault(r.Slug)))
                .ToList();
        }

        private async Task<Dictionary<string, IReadOnlySet<string>>> LoadPermissionsByRoleAsync(IEnumerable<string> slugs, string organizationId)
        {
            var wanted = slugs.Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

            if (wanted.Count == 0)
            {
                return result;
            }

            var permissions = await _resourceRepository.GetActivePermissionsForRolesAsync(wanted, organizationId) ?? [];

            foreach (var slug in wanted)
            {
                result[slug] = permissions
                    .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Resource)
                        && (p.Roles ?? []).Contains(slug, StringComparer.OrdinalIgnoreCase))
                    .Select(p => p.Resource)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            return result;
        }
    }
}
