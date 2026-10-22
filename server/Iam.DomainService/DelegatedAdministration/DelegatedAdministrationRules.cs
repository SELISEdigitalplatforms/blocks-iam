using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.DelegatedAdministration
{
    /// <summary>
    /// "Grant only what you hold": rules R1-R10 as pure functions of a <see cref="GrantScope"/>.
    /// </summary>
    /// <remarks>
    /// Every check returns null when it passes and a <see cref="DelegationDenial"/> when it would
    /// refuse; whether a denial refuses the request or is only logged is the service's call
    /// (<see cref="DelegatedAdministrationMode"/>). Every check passes for the system user, so a
    /// single-organization tenant -- whose callers always resolve to <c>default</c> -- is never
    /// affected. Peers are allowed (same power passes), several roles are a union, and a slug the
    /// organization does not know is never a source of power.
    /// </remarks>
    public static class DelegatedAdministrationRules
    {
        public const string AssignRole = "R1-AssignRole";
        public const string GrantPermission = "R2-GrantPermission";
        public const string ManageUser = "R3-ManageUser";
        public const string AccessDelta = "R4-AccessDelta";
        public const string EditRole = "R5-EditRole";
        public const string SystemOnly = "R6-SystemOnly";
        public const string CreateOrganization = "R7-CreateOrganization";
        public const string ClientCredential = "R8-ClientCredential";
        public const string SignupLink = "R9-SignupLink";
        public const string AccessAttribute = "R10-AccessAttributes";

        /// <summary>
        /// R1. Role R may be assigned when it is a live role in O, it is below a role the caller
        /// holds if it has a parent, and every permission it carries is one the caller holds.
        /// </summary>
        public static DelegationDenial? CheckAssignRole(GrantScope scope, string slug, string rule = AssignRole)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem)
            {
                return null;
            }

            var role = scope.FindRole(slug);
            if (role is null)
            {
                return new DelegationDenial(rule, slug ?? string.Empty, "role_not_found_or_archived");
            }

            if (!string.IsNullOrWhiteSpace(role.ParentRoleSlug) && !scope.ManagedRoles.Contains(role.Slug))
            {
                return new DelegationDenial(rule, role.Slug, "managed_role_outside_caller_tree");
            }

            return scope.PermissionsOf(role.Slug).Any(p => !scope.HeldPermissions.Contains(p))
                ? new DelegationDenial(rule, role.Slug, "role_carries_permission_not_held")
                : null;
        }

        /// <summary>R2. Permission P may be granted (to a user, role or client credential) when the caller holds it.</summary>
        public static DelegationDenial? CheckGrantPermission(GrantScope scope, string resource, string rule = GrantPermission)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem)
            {
                return null;
            }

            return !string.IsNullOrWhiteSpace(resource) && scope.HeldPermissions.Contains(resource)
                ? null
                : new DelegationDenial(rule, resource ?? string.Empty, "permission_not_held");
        }

        /// <summary>R1 on every role and R2 on every permission; the first denial wins.</summary>
        public static DelegationDenial? CheckGrant(
            GrantScope scope,
            IEnumerable<string>? roles,
            IEnumerable<string>? permissions,
            string? rule = null)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem)
            {
                return null;
            }

            return Clean(roles)
                    .Select(slug => CheckAssignRole(scope, slug, rule ?? AssignRole))
                    .FirstOrDefault(d => d is not null)
                ?? Clean(permissions)
                    .Select(resource => CheckGrantPermission(scope, resource, rule ?? GrantPermission))
                    .FirstOrDefault(d => d is not null);
        }

        /// <summary>
        /// R3. User U may be managed when U is not the caller, U is a member of O, every live role
        /// U holds in O passes R1 and every direct permission U holds in O passes R2.
        /// </summary>
        /// <param name="scope">The caller's power.</param>
        /// <param name="user">The user acted on, as stored before the change.</param>
        /// <param name="requireMembership">
        /// False on the paths that add U to O (users/access, the existing-email create, bulk): a
        /// non-member holds nothing in O, so there is nothing above the caller to protect.
        /// </param>
        public static DelegationDenial? CheckManageUser(GrantScope scope, User user, bool requireMembership = true)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(user);
            if (scope.IsSystem)
            {
                return null;
            }

            if (string.Equals(user.ItemId, scope.CallerId, StringComparison.Ordinal))
            {
                return new DelegationDenial(ManageUser, user.ItemId, "cannot_manage_self");
            }

            var organizationId = scope.OrganizationId;
            if (requireMembership && !IsMember(user, organizationId))
            {
                return new DelegationDenial(ManageUser, user.ItemId, "user_not_member_of_organization");
            }

            // Unknown and archived slugs are ignored: they grant nothing, so they cannot put U
            // above the caller.
            var holdsRoleAboveCaller = GrantsIn(user.Roles, organizationId)
                .Where(slug => scope.FindRole(slug) is not null)
                .Any(slug => CheckAssignRole(scope, slug) is not null);
            if (holdsRoleAboveCaller)
            {
                return new DelegationDenial(ManageUser, user.ItemId, "user_holds_role_above_caller");
            }

            // Direct permissions are not filtered by the catalog: a direct grant mints a claim even
            // after its permission is archived, so it is power whether or not the catalog lists it.
            return GrantsIn(user.Permissions, organizationId).Any(p => !scope.HeldPermissions.Contains(p))
                ? new DelegationDenial(ManageUser, user.ItemId, "user_holds_permission_above_caller")
                : null;
        }

        /// <summary>
        /// R4 with R3. A list replace (users/access, the existing-email create) is checked on its
        /// delta only: every added or removed role passes R1, every added or removed permission
        /// passes R2. R3 applies to U as stored before the change. An empty request list keeps what
        /// is stored, exactly as the write does.
        /// </summary>
        public static DelegationDenial? CheckAccessChange(
            GrantScope scope,
            User user,
            IReadOnlyCollection<string>? requestedRoles,
            IReadOnlyCollection<string>? requestedPermissions)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(user);
            if (scope.IsSystem)
            {
                return null;
            }

            var manage = CheckManageUser(scope, user, requireMembership: false);
            if (manage is not null)
            {
                return manage;
            }

            var (addedRoles, removedRoles) = Delta(GrantsIn(user.Roles, scope.OrganizationId), requestedRoles);
            var (addedPermissions, removedPermissions) = Delta(GrantsIn(user.Permissions, scope.OrganizationId), requestedPermissions);

            return CheckRoleDelta(scope, addedRoles, removedRoles)
                ?? addedPermissions.Concat(removedPermissions)
                    .Select(p => CheckGrantPermission(scope, p, AccessDelta))
                    .FirstOrDefault(d => d is not null);
        }

        /// <summary>
        /// R1 on a role delta. An added role must be live; a removed role the organization no
        /// longer knows is skipped, since taking away a slug that grants nothing grants nothing.
        /// </summary>
        public static DelegationDenial? CheckRoleDelta(GrantScope scope, IEnumerable<string>? addedRoles, IEnumerable<string>? removedRoles)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem)
            {
                return null;
            }

            return Clean(addedRoles).Select(slug => CheckAssignRole(scope, slug, AccessDelta)).FirstOrDefault(d => d is not null)
                ?? Clean(removedRoles)
                    .Where(slug => scope.FindRole(slug) is not null)
                    .Select(slug => CheckAssignRole(scope, slug, AccessDelta))
                    .FirstOrDefault(d => d is not null);
        }

        /// <summary>R5, create. A new role is empty, so only its parent matters: one the caller holds or manages.</summary>
        public static DelegationDenial? CheckCreateRole(GrantScope scope, string? parentSlug)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem || string.IsNullOrWhiteSpace(parentSlug))
            {
                return null;
            }

            return scope.HoldsOrManages(parentSlug)
                ? null
                : new DelegationDenial(EditRole, parentSlug, "parent_not_held_or_managed");
        }

        /// <summary>
        /// R5, edit (update, archive, assign/unassign permissions, change parent). The caller may
        /// not edit a role it holds; the role must pass R1 before (and, through the parent and
        /// permission rules, after); every added or removed permission passes R2; a new parent must
        /// be held or managed.
        /// </summary>
        /// <param name="scope">The caller's power.</param>
        /// <param name="role">The role as stored.</param>
        /// <param name="newParentSlug">The parent after the change; only checked when <paramref name="parentChanged"/>.</param>
        /// <param name="parentChanged">True when the request moves the role.</param>
        /// <param name="changedPermissions">Resources added to or removed from the role.</param>
        public static DelegationDenial? CheckEditRole(
            GrantScope scope,
            Role role,
            string? newParentSlug = null,
            bool parentChanged = false,
            IEnumerable<string>? changedPermissions = null)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(role);
            if (scope.IsSystem)
            {
                return null;
            }

            if (scope.HeldRoles.Contains(role.Slug))
            {
                return new DelegationDenial(EditRole, role.Slug, "cannot_edit_own_role");
            }

            var before = CheckAssignRole(scope, role.Slug, EditRole);
            if (before is not null)
            {
                return before;
            }

            if (parentChanged && !string.IsNullOrWhiteSpace(newParentSlug) && !scope.HoldsOrManages(newParentSlug))
            {
                return new DelegationDenial(EditRole, role.Slug, "parent_not_held_or_managed");
            }

            return Clean(changedPermissions)
                .Select(p => CheckGrantPermission(scope, p, EditRole))
                .FirstOrDefault(d => d is not null);
        }

        /// <summary>R6. Permissions, tenant configuration and signup settings belong to the system user.</summary>
        public static DelegationDenial? CheckSystemOnly(GrantScope scope, string target)
        {
            ArgumentNullException.ThrowIfNull(scope);
            return scope.IsSystem ? null : new DelegationDenial(SystemOnly, target, "system_user_only");
        }

        /// <summary>
        /// R10. A non-system caller may change the values of access attribute <c>name</c> in O
        /// only within its own values for that name, and may not touch another organization's.
        /// </summary>
        /// <param name="scope">The caller's power.</param>
        /// <param name="targetId">The user the attributes belong to, for the log.</param>
        /// <param name="before">The stored bag (null for a new user).</param>
        /// <param name="after">The bag that would be stored; null when the request leaves attributes alone.</param>
        public static DelegationDenial? CheckAccessAttributes(
            GrantScope scope,
            string targetId,
            Dictionary<string, object>? before,
            Dictionary<string, object>? after)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem || after is null)
            {
                return null;
            }

            if (AccessAttributes.IsMalformed(after))
            {
                return new DelegationDenial(AccessAttribute, targetId, "access_attributes_malformed");
            }

            var stored = AccessAttributes.Read(before);
            var requested = AccessAttributes.Read(after);

            return stored.Keys.Union(requested.Keys, StringComparer.Ordinal)
                .Select(organizationId => CheckOrganizationAccessAttributes(
                    scope,
                    targetId,
                    organizationId,
                    stored.GetValueOrDefault(organizationId),
                    requested.GetValueOrDefault(organizationId)))
                .FirstOrDefault(d => d is not null);
        }

        /// <summary>R10 for one organization's access attributes: every changed value must be the caller's own.</summary>
        private static DelegationDenial? CheckOrganizationAccessAttributes(
            GrantScope scope,
            string targetId,
            string organizationId,
            Dictionary<string, HashSet<string>>? storedNames,
            Dictionary<string, HashSet<string>>? requestedNames)
        {
            storedNames ??= new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            requestedNames ??= new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var isOwnOrganization = string.Equals(organizationId, scope.OrganizationId, StringComparison.Ordinal);

            foreach (var name in storedNames.Keys.Union(requestedNames.Keys, StringComparer.Ordinal))
            {
                var changed = new HashSet<string>(storedNames.GetValueOrDefault(name) ?? [], StringComparer.Ordinal);
                changed.SymmetricExceptWith(requestedNames.GetValueOrDefault(name) ?? []);
                if (changed.Count == 0)
                {
                    continue;
                }

                if (!isOwnOrganization)
                {
                    return new DelegationDenial(AccessAttribute, targetId, "access_attributes_of_other_organization");
                }

                var own = scope.CallerAccessValues(name);
                if (changed.Any(value => !own.Contains(value)))
                {
                    return new DelegationDenial(AccessAttribute, targetId, "access_attribute_value_not_held");
                }
            }

            return null;
        }

        /// <summary>
        /// True when making <paramref name="newParentSlug"/> the parent of <paramref name="slug"/>
        /// would close a loop: the parent is the role itself or already below it.
        /// </summary>
        /// <param name="slug">The role being moved.</param>
        /// <param name="newParentSlug">The proposed parent.</param>
        /// <param name="newParentAncestors">The proposed parent's own ancestor chain.</param>
        public static bool CreatesCycle(string slug, string? newParentSlug, IEnumerable<string>? newParentAncestors)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(newParentSlug))
            {
                return false;
            }

            return string.Equals(slug, newParentSlug, StringComparison.OrdinalIgnoreCase)
                || (newParentAncestors ?? []).Contains(slug, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The three-way membership test of <c>OrganizationAccessResolver.HasOrganizationAccess</c>.</summary>
        public static bool IsMember(User user, string organizationId)
        {
            ArgumentNullException.ThrowIfNull(user);
            return (user.OrganizationIds ?? []).Contains(organizationId)
                || (user.Roles?.ContainsKey(organizationId) ?? false)
                || (user.Permissions?.ContainsKey(organizationId) ?? false);
        }

        /// <summary>
        /// What a list replace adds and removes. An empty or null request keeps what is stored, so
        /// it changes nothing -- matching <c>GrantOrganization</c>.
        /// </summary>
        public static (List<string> Added, List<string> Removed) Delta(
            IEnumerable<string> stored,
            IReadOnlyCollection<string>? requested)
        {
            ArgumentNullException.ThrowIfNull(stored);
            var current = new HashSet<string>(Clean(stored), StringComparer.OrdinalIgnoreCase);
            if (requested is null || requested.Count == 0)
            {
                return ([], []);
            }

            var next = new HashSet<string>(Clean(requested), StringComparer.OrdinalIgnoreCase);
            return (next.Where(x => !current.Contains(x)).ToList(), current.Where(x => !next.Contains(x)).ToList());
        }

        /// <summary>The user's grants in one organization, blank entries dropped.</summary>
        public static IEnumerable<string> GrantsIn(Dictionary<string, List<string>>? grants, string organizationId) =>
            grants is not null && grants.TryGetValue(organizationId, out var inScope) && inScope is not null
                ? Clean(inScope)
                : [];

        private static IEnumerable<string> Clean(IEnumerable<string>? values) =>
            (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
