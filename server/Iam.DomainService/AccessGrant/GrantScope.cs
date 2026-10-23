using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.AccessGrant
{
    /// <summary>Who is asking, as the access grant rules see it.</summary>
    /// <param name="UserId">The caller's user id; empty for a call with no user (workers, system).</param>
    /// <param name="OrganizationId">The caller's effective organization; blank reads as <c>default</c>.</param>
    /// <param name="IsSystem">
    /// True for the system user: effective organization <c>default</c>, no user, or a root-tenant /
    /// impersonated platform call. No rule applies to it.
    /// </param>
    public sealed record GrantCaller(string UserId, string OrganizationId, bool IsSystem);

    /// <summary>Why a rule would refuse. Server log only; the caller only ever sees the generic error.</summary>
    /// <param name="Rule">The rule that refused (R1-R12).</param>
    /// <param name="Target">What the rule was applied to: a role slug, permission, user id or endpoint.</param>
    /// <param name="Reason">A short, fixed reason code.</param>
    public sealed record GrantDenial(string Rule, string Target, string Reason);

    /// <summary>
    /// The caller's power in one organization, read from the database, never from the token.
    /// </summary>
    /// <remarks>
    /// Built once per request (or once per bulk chunk) and then asked many in-memory questions, so
    /// a check costs no further reads. The organization's role and permission catalog is held
    /// alongside the caller's power because almost every rule needs <c>Permissions(R)</c>.
    /// </remarks>
    public sealed class GrantScope
    {
        private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Role> _roles;
        private readonly Dictionary<string, HashSet<string>> _rolePermissions;
        private readonly Dictionary<string, string> _permissionResourcesById;
        private readonly Dictionary<string, HashSet<string>> _callerAccessAttributes;

        private GrantScope(
            GrantCaller caller,
            Dictionary<string, Role> roles,
            Dictionary<string, HashSet<string>> rolePermissions,
            Dictionary<string, string> permissionResourcesById,
            Dictionary<string, HashSet<string>> callerAccessAttributes)
        {
            Caller = caller;
            _roles = roles;
            _rolePermissions = rolePermissions;
            _permissionResourcesById = permissionResourcesById;
            _callerAccessAttributes = callerAccessAttributes;
            HeldRoles = Empty;
            ManagedRoles = Empty;
            HeldPermissions = Empty;
        }

        public GrantCaller Caller { get; }

        public bool IsSystem => Caller.IsSystem;

        public string CallerId => Caller.UserId;

        public string OrganizationId => Caller.OrganizationId;

        /// <summary><c>User.Roles[O]</c> minus unknown and archived slugs.</summary>
        public IReadOnlySet<string> HeldRoles { get; private init; }

        /// <summary>Roles in O whose <c>AncestorRoleSlugs</c> contains a held role.</summary>
        public IReadOnlySet<string> ManagedRoles { get; private init; }

        /// <summary>Permissions of every held and managed role, plus the caller's direct permissions in O.</summary>
        public IReadOnlySet<string> HeldPermissions { get; private init; }

        /// <summary>Every non-archived role in O, by slug.</summary>
        public IEnumerable<Role> Roles => _roles.Values;

        /// <summary>
        /// A scope that holds nothing and made no reads: the system user's (no rule applies to it),
        /// or a non-system caller's for a check that needs only who the caller is (R6).
        /// </summary>
        public static GrantScope WithoutPower(GrantCaller caller) =>
            new(caller, new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase),
                new(StringComparer.Ordinal), new(StringComparer.Ordinal));

        /// <summary>
        /// Computes the caller's power in <see cref="GrantCaller.OrganizationId"/>.
        /// </summary>
        /// <param name="caller">The caller; must not be the system user.</param>
        /// <param name="callerUser">The caller's user document, or null when it no longer exists (holds nothing).</param>
        /// <param name="roles">Non-archived roles of the organization.</param>
        /// <param name="permissions">Non-archived permissions of the organization.</param>
        public static GrantScope Build(
            GrantCaller caller,
            User? callerUser,
            IEnumerable<Role> roles,
            IEnumerable<Permission> permissions)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(roles);
            ArgumentNullException.ThrowIfNull(permissions);

            var organizationId = caller.OrganizationId;

            var roleMap = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in roles.Where(r => r is not null && !r.IsArchived && !string.IsNullOrWhiteSpace(r.Slug)))
            {
                roleMap[role.Slug] = role;
            }

            var (rolePermissions, resourcesById) = IndexPermissions(permissions);

            var held = new HashSet<string>(
                AccessGrantRules.GrantsIn(callerUser?.Roles, organizationId).Where(roleMap.ContainsKey),
                StringComparer.OrdinalIgnoreCase);

            var managed = new HashSet<string>(
                roleMap.Values
                    .Where(r => (r.AncestorRoleSlugs ?? []).Any(held.Contains))
                    .Select(r => r.Slug),
                StringComparer.OrdinalIgnoreCase);

            var heldPermissions = new HashSet<string>(
                held.Concat(managed).SelectMany(slug => rolePermissions.GetValueOrDefault(slug) ?? []),
                StringComparer.OrdinalIgnoreCase);
            heldPermissions.UnionWith(AccessGrantRules.GrantsIn(callerUser?.Permissions, organizationId));

            var callerAccess = AccessAttributes.Read(callerUser?.Attributes)
                .TryGetValue(organizationId, out var ownValues)
                    ? ownValues
                    : new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            return new GrantScope(caller, roleMap, rolePermissions, resourcesById, callerAccess)
            {
                HeldRoles = held,
                ManagedRoles = managed,
                HeldPermissions = heldPermissions
            };
        }

        /// <summary>Role slug → resources it grants, and permission item id → resource, over live permissions.</summary>
        private static (Dictionary<string, HashSet<string>> RolePermissions, Dictionary<string, string> ResourcesById) IndexPermissions(
            IEnumerable<Permission> permissions)
        {
            var rolePermissions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var resourcesById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var permission in permissions.Where(p => p is not null && !p.IsArchived && !string.IsNullOrWhiteSpace(p.Resource)))
            {
                if (!string.IsNullOrWhiteSpace(permission.ItemId))
                {
                    resourcesById[permission.ItemId] = permission.Resource;
                }

                foreach (var slug in (permission.Roles ?? []).Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    if (!rolePermissions.TryGetValue(slug, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        rolePermissions[slug] = set;
                    }

                    set.Add(permission.Resource);
                }
            }

            return (rolePermissions, resourcesById);
        }

        /// <summary>The non-archived role in O with this slug, or null (unknown or archived).</summary>
        public Role? FindRole(string? slug) =>
            !string.IsNullOrWhiteSpace(slug) && _roles.TryGetValue(slug, out var role) ? role : null;

        /// <summary><c>Permissions(R)</c>: the resource of every non-archived permission in O naming the role.</summary>
        public IReadOnlySet<string> PermissionsOf(string slug) =>
            _rolePermissions.TryGetValue(slug, out var set) ? set : Empty;

        /// <summary>The resource of a non-archived permission in O by its item id, or null.</summary>
        public string? ResourceOfPermissionId(string? itemId) =>
            !string.IsNullOrWhiteSpace(itemId) && _permissionResourcesById.TryGetValue(itemId, out var resource) ? resource : null;

        /// <summary>The caller's own access-attribute values for <paramref name="name"/> in O.</summary>
        public IReadOnlySet<string> CallerAccessValues(string name) =>
            _callerAccessAttributes.TryGetValue(name, out var values) ? values : Empty;

        /// <summary>True when <paramref name="slug"/> is held by the caller or is below a held role.</summary>
        public bool HoldsOrManages(string slug) => HeldRoles.Contains(slug) || ManagedRoles.Contains(slug);
    }
}
