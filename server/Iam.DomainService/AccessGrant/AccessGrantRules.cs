using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.AccessGrant
{
    /// <summary>
    /// Position in the role tree decides power: rules R1-R10 as pure functions of a <see cref="GrantScope"/>.
    /// </summary>
    /// <remarks>
    /// Every check returns null when it passes and a <see cref="GrantDenial"/> when it would
    /// refuse; the service logs it and refuses the request. Every check passes for the system user, so a
    /// single-organization tenant -- whose callers always resolve to <c>default</c> -- is never
    /// affected. Any other caller is a top user (its manage power comes from a standalone role or a
    /// direct permission: the organization's owner) or a descendant (acts only below its own roles).
    /// Nobody edits itself or a role it holds, copies from <c>default</c> are read-only outside
    /// <c>default</c>, and a slug the organization does not know is never a source of power.
    /// </remarks>
    public static class AccessGrantRules
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

        /// <summary>The permission that guards role create, edit, archive and permission changes.</summary>
        public const string MutateRolesPermission = "blocks-iam::iam::mutate-roles";

        /// <summary>The permission that guards user create, access changes, activation and deactivation.</summary>
        public const string MutateUsersPermission = "blocks-iam::iam::mutate-users";

        /// <summary>True for a permission that gives the power to manage roles or users.</summary>
        public static bool IsManagePermission(string? resource) =>
            string.Equals(resource, MutateRolesPermission, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resource, MutateUsersPermission, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// R1. Role R may be given when it is a live role in O and the caller is a top user, or R is
        /// below a role the caller holds. A copy from <c>default</c> may be given like any role.
        /// </summary>
        public static GrantDenial? CheckAssignRole(GrantScope scope, string slug, string rule = AssignRole)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem)
            {
                return null;
            }

            var role = scope.FindRole(slug);
            if (role is null)
            {
                return new GrantDenial(rule, slug ?? string.Empty, "role_not_found_or_archived");
            }

            return scope.IsTop || scope.ManagedRoles.Contains(role.Slug)
                ? null
                : new GrantDenial(rule, role.Slug, "role_not_below_caller");
        }

        /// <summary>
        /// R2. Permission P may be given directly (to a user or a client credential) only by a top
        /// user. A descendant gives power through roles below it, never as a direct permission.
        /// </summary>
        public static GrantDenial? CheckGrantPermission(GrantScope scope, string resource, string rule = GrantPermission)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(resource))
            {
                return new GrantDenial(rule, string.Empty, "permission_empty");
            }

            return scope.IsTop ? null : new GrantDenial(rule, resource, "descendant_cannot_grant_direct_permission");
        }

        /// <summary>
        /// R2 for a role. A top user may put any permission on a role it may edit; a descendant only
        /// one its own roles carry (or it holds directly), so a role below it is never stronger than it.
        /// </summary>
        public static GrantDenial? CheckRolePermission(GrantScope scope, string resource, string rule = EditRole)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem || scope.IsTop)
            {
                return null;
            }

            return !string.IsNullOrWhiteSpace(resource) && scope.OwnPermissions.Contains(resource)
                ? null
                : new GrantDenial(rule, resource ?? string.Empty, "permission_not_on_own_role");
        }

        /// <summary>R1 on every role and R2 on every permission; the first denial wins.</summary>
        public static GrantDenial? CheckGrant(
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
        /// R3. User U may be managed when U is not the caller and U is a member of O. A top user may
        /// manage every member; a descendant only a user whose every live role in O is below the
        /// caller (or is a role the caller holds too that carries no manage power, such as a base
        /// role everybody has) and whose direct permissions in O the caller's own roles carry.
        /// </summary>
        /// <param name="scope">The caller's power.</param>
        /// <param name="user">The user acted on, as stored before the change.</param>
        /// <param name="requireMembership">
        /// False on the paths that add U to O (users/access, the existing-email create, bulk): a
        /// non-member holds nothing in O, so there is nothing above the caller to protect.
        /// </param>
        public static GrantDenial? CheckManageUser(GrantScope scope, User user, bool requireMembership = true)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(user);
            if (scope.IsSystem)
            {
                return null;
            }

            if (string.Equals(user.ItemId, scope.CallerId, StringComparison.Ordinal))
            {
                return new GrantDenial(ManageUser, user.ItemId, "cannot_manage_self");
            }

            var organizationId = scope.OrganizationId;
            if (requireMembership && !IsMember(user, organizationId))
            {
                return new GrantDenial(ManageUser, user.ItemId, "user_not_member_of_organization");
            }

            return CheckManageHolder(
                scope,
                GrantsIn(user.Roles, organizationId),
                GrantsIn(user.Permissions, organizationId),
                ManageUser,
                user.ItemId);
        }

        /// <summary>
        /// Whether the caller may manage something that holds these roles and direct permissions in
        /// O (a user or a client credential). A top user may; a descendant only when every live role
        /// is below it (or a role it holds too that gives no power to manage) and every direct
        /// permission is one its own roles carry.
        /// </summary>
        public static GrantDenial? CheckManageHolder(
            GrantScope scope,
            IEnumerable<string>? roles,
            IEnumerable<string>? permissions,
            string rule,
            string target)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem || scope.IsTop)
            {
                return null;
            }

            // Unknown and archived slugs are ignored: they grant nothing, so they cannot put the
            // holder above the caller.
            if (Clean(roles).Where(slug => scope.FindRole(slug) is not null).Any(slug => !IsBelowOrSharedBase(scope, slug)))
            {
                return new GrantDenial(rule, target, "holds_role_not_below_caller");
            }

            // Direct permissions are not filtered by the catalog: a direct grant mints a claim even
            // after its permission is archived, so it is power whether or not the catalog lists it.
            return Clean(permissions).Any(p => !scope.OwnPermissions.Contains(p))
                ? new GrantDenial(rule, target, "holds_permission_above_caller")
                : null;
        }

        /// <summary>A role below the caller, or one the caller holds too that gives no power to manage.</summary>
        private static bool IsBelowOrSharedBase(GrantScope scope, string slug) =>
            scope.ManagedRoles.Contains(slug)
            || (scope.HeldRoles.Contains(slug) && !scope.CarriesManagePower(slug));

        /// <summary>
        /// R4 with R3. A list replace (users/access, the existing-email create) is checked on its
        /// delta only: every added or removed role passes R1, every added or removed permission
        /// passes R2. R3 applies to U as stored before the change. An empty request list keeps what
        /// is stored, exactly as the write does.
        /// </summary>
        public static GrantDenial? CheckAccessChange(
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
        public static GrantDenial? CheckRoleDelta(GrantScope scope, IEnumerable<string>? addedRoles, IEnumerable<string>? removedRoles)
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

        /// <summary>
        /// R5, create. A top user may create a standalone role or one under any live role in O. A
        /// descendant must create it under one of its power roles or a role below them, never standalone.
        /// </summary>
        public static GrantDenial? CheckCreateRole(GrantScope scope, string? parentSlug)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(parentSlug))
            {
                return scope.IsTop ? null : new GrantDenial(EditRole, string.Empty, "descendant_must_create_below_own_role");
            }

            if (scope.FindRole(parentSlug) is null)
            {
                return new GrantDenial(EditRole, parentSlug, "parent_not_found_or_archived");
            }

            return scope.IsTop || scope.MayParent(parentSlug)
                ? null
                : new GrantDenial(EditRole, parentSlug, "parent_not_held_or_managed");
        }

        /// <summary>
        /// R5, edit (update, archive, assign/unassign permissions, change parent). Never a copy from
        /// <c>default</c> and never a role the caller holds. A top user may then edit any role of O:
        /// any parent, any permission. A descendant only a role below it, kept below it, and may add
        /// only permissions its own roles carry.
        /// </summary>
        /// <param name="scope">The caller's power.</param>
        /// <param name="role">The role as stored.</param>
        /// <param name="newParentSlug">The parent after the change; only checked when <paramref name="parentChanged"/>.</param>
        /// <param name="parentChanged">True when the request moves the role.</param>
        /// <param name="addedPermissions">Resources added to the role. Taking a permission away never raises power.</param>
        public static GrantDenial? CheckEditRole(
            GrantScope scope,
            Role role,
            string? newParentSlug = null,
            bool parentChanged = false,
            IEnumerable<string>? addedPermissions = null)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(role);
            if (scope.IsSystem)
            {
                return null;
            }

            if (role.CreatedFromDefault)
            {
                return new GrantDenial(EditRole, role.Slug, "default_copy_is_read_only");
            }

            if (scope.HeldRoles.Contains(role.Slug))
            {
                return new GrantDenial(EditRole, role.Slug, "cannot_edit_own_role");
            }

            if (!scope.IsTop && !scope.ManagedRoles.Contains(role.Slug))
            {
                return new GrantDenial(EditRole, role.Slug, "role_not_below_caller");
            }

            var parentDenial = parentChanged ? CheckNewParent(scope, role, newParentSlug) : null;
            return parentDenial
                ?? Clean(addedPermissions)
                    .Select(p => CheckRolePermission(scope, p))
                    .FirstOrDefault(d => d is not null);
        }

        /// <summary>A moved role: a top user may put it anywhere; a descendant must keep it under one of its power roles or below them.</summary>
        private static GrantDenial? CheckNewParent(GrantScope scope, Role role, string? newParentSlug)
        {
            if (string.IsNullOrWhiteSpace(newParentSlug))
            {
                return scope.IsTop ? null : new GrantDenial(EditRole, role.Slug, "descendant_cannot_move_role_to_top");
            }

            return scope.IsTop || scope.MayParent(newParentSlug)
                ? null
                : new GrantDenial(EditRole, role.Slug, "parent_not_held_or_managed");
        }

        /// <summary>R6. Permissions, tenant configuration and signup settings belong to the system user.</summary>
        public static GrantDenial? CheckSystemOnly(GrantScope scope, string target)
        {
            ArgumentNullException.ThrowIfNull(scope);
            return scope.IsSystem ? null : new GrantDenial(SystemOnly, target, "system_user_only");
        }

        /// <summary>
        /// R10. A non-system caller may change the values of access attribute <c>name</c> in O
        /// only within its own values for that name, and may not touch another organization's.
        /// </summary>
        /// <param name="scope">The caller's power.</param>
        /// <param name="targetId">The user the attributes belong to, for the log.</param>
        /// <param name="before">The stored bag (null for a new user).</param>
        /// <param name="after">The bag that would be stored; null when the request leaves attributes alone.</param>
        public static GrantDenial? CheckAccessAttributes(
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
                return new GrantDenial(AccessAttribute, targetId, "access_attributes_malformed");
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
        private static GrantDenial? CheckOrganizationAccessAttributes(
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
                    return new GrantDenial(AccessAttribute, targetId, "access_attributes_of_other_organization");
                }

                var own = scope.CallerAccessValues(name);
                if (changed.Any(value => !own.Contains(value)))
                {
                    return new GrantDenial(AccessAttribute, targetId, "access_attribute_value_not_held");
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

        /// <summary>The values without blanks and case-insensitive duplicates.</summary>
        public static IEnumerable<string> Clean(IEnumerable<string>? values) =>
            (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
