using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Utilities
{
    /// <summary>Who is asking, as far as user and role administration is concerned.</summary>
    public enum CallerKind
    {
        /// <summary>The token names no usable organization, or a different one from the target. Reaches nothing.</summary>
        Denied,

        /// <summary>
        /// No authenticated identity: anonymous signup, queue consumers, the SSO consent exchange.
        /// These flows take their organization from trusted configuration, never from a caller.
        /// </summary>
        System,

        /// <summary>
        /// Above every organization: the tenant-wide <c>default</c> scope of a multi-organization
        /// tenant, or an impersonating project owner. Single-organization tenants have none -- there
        /// <c>default</c> is simply the one organization.
        /// </summary>
        Supreme,

        /// <summary>A client-credential token: an application backend acting inside its own organization.</summary>
        Machine,

        /// <summary>A signed-in person, bound by the role hierarchy and the access lists.</summary>
        User
    }

    public enum UserAccessLevel
    {
        None = 0,
        View = 1,
        Manage = 2
    }

    /// <summary>
    /// Everything the access rules need to know about the caller, resolved once per request by
    /// <c>AccessPolicyService</c> so the rules themselves stay pure and testable without a database.
    /// </summary>
    public sealed class CallerAccess
    {
        public CallerKind Kind { get; init; }

        /// <summary>The organization every decision is made in. Empty only when <see cref="Kind"/> is <see cref="CallerKind.Denied"/>.</summary>
        public string OrganizationId { get; init; } = string.Empty;

        public string UserId { get; init; } = string.Empty;

        public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Direct permissions plus every permission granted to one of <see cref="Roles"/> in the organization.</summary>
        public IReadOnlySet<string> EffectivePermissions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Permissions carried in the token itself, without the ones granted through roles.</summary>
        public IReadOnlySet<string> DirectPermissions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The organization's non-archived roles.</summary>
        public IReadOnlyList<Role> OrganizationRoles { get; init; } = [];

        public bool IsMultiOrgEnabled { get; init; }

        /// <summary>
        /// Whether the organization has built a role hierarchy (see <see cref="UserAccessPolicy.UsesHierarchy"/>).
        /// Everything this policy adds -- access lists, grant rules, self-edit guards, hidden fields --
        /// applies only then; an organization without one behaves exactly as before.
        /// </summary>
        public bool UsesHierarchy { get; init; }

        /// <summary>True when the caller holds one of the organization's top roles.</summary>
        public bool IsTopRoleHolder { get; init; }

        /// <summary>
        /// Callers the role hierarchy does not bind at all -- trusted flows, callers above every
        /// organization, application backends. They may grant any existing role of the organization.
        /// </summary>
        public bool BypassesHierarchy =>
            Kind is CallerKind.System or CallerKind.Supreme or CallerKind.Machine;

        /// <summary>
        /// Callers who reach every member of the organization and may change the role and permission
        /// entries of access lists: the hierarchy-bypassing ones, and holders of a top role. Granting
        /// is not included -- a top role still grants only its own tree, since an organization may
        /// hold several independent trees.
        /// </summary>
        public bool IsPrivileged => BypassesHierarchy || IsTopRoleHolder;

        public static CallerAccess Denied { get; } = new() { Kind = CallerKind.Denied };
    }

    /// <summary>The fields of a user the access rules read, whichever document shape it was loaded as.</summary>
    public sealed record AccessSubject(
        string ItemId,
        IReadOnlyCollection<string> OrganizationIds,
        IReadOnlyDictionary<string, List<string>> Roles,
        IReadOnlyDictionary<string, List<string>> Permissions,
        IReadOnlyDictionary<string, UserAccessList> AllowedToView,
        IReadOnlyDictionary<string, UserAccessList> AllowedToManage,
        string? CreatedBy = null)
    {
        public static AccessSubject From(User user) => new(
            user.ItemId ?? string.Empty,
            user.OrganizationIds ?? [],
            user.Roles ?? new(),
            user.Permissions ?? new(),
            user.AllowedToView ?? new(),
            user.AllowedToManage ?? new(),
            user.CreatedBy);

        public static AccessSubject From(GetAccounts user) => new(
            user.ItemId ?? string.Empty,
            user.OrganizationIds ?? [],
            user.Roles ?? new(),
            user.Permissions ?? new(),
            user.AllowedToView ?? new(),
            user.AllowedToManage ?? new(),
            user.CreatedBy);

        /// <summary>
        /// The manage list that applies in <paramref name="organizationId"/>. A member who joined
        /// before access lists existed has none stored, and is treated as managed by whoever created
        /// the account -- so existing tenants need no migration before switching enforcement on.
        /// </summary>
        public UserAccessList? ManageListFor(string organizationId)
        {
            if (AllowedToManage.TryGetValue(organizationId, out var stored))
            {
                return stored;
            }

            return string.IsNullOrWhiteSpace(CreatedBy) || string.Equals(CreatedBy, ItemId, StringComparison.Ordinal)
                ? null
                : new UserAccessList { Users = [CreatedBy] };
        }
    }

    /// <summary>
    /// The one place that decides who may see, manage and grant what inside an organization.
    /// </summary>
    /// <remarks>
    /// Pure: every input arrives in a <see cref="CallerAccess"/> or as an argument, so the whole rule
    /// set is unit-testable and the services cannot drift from each other.
    /// <para>
    /// The role hierarchy is not extended. <c>ParentRoleSlug</c> / <c>AncestorRoleSlugs</c> /
    /// <c>CanCreateOwn</c> already say who sits above whom; that is used twice here -- to decide
    /// which roles a caller may grant, and to fill a new member's access list with the roles above
    /// whoever added them.
    /// </para>
    /// </remarks>
    public static class UserAccessPolicy
    {
        /// <summary>Individually listed principals per list. Groups of people belong in roles.</summary>
        public const int MaxUsersPerAccessList = 50;

        /// <summary>
        /// A top role heads the organization's hierarchy: no parent, and it may create roles below
        /// it. A role without a parent that cannot create roles (an "auditor") is standalone, not top.
        /// </summary>
        public static bool IsTopRole(Role role) =>
            role is not null
            && !role.IsArchived
            && string.IsNullOrWhiteSpace(role.ParentRoleSlug)
            && role.CanCreateOwn;

        public static IReadOnlyList<Role> TopRoles(IEnumerable<Role>? organizationRoles) =>
            (organizationRoles ?? []).Where(IsTopRole).ToList();

        /// <summary>
        /// An organization uses the hierarchy once one of its roles has a parent -- something only an
        /// administrator building a hierarchy does. <c>CanCreateOwn</c> alone is no signal: the entity
        /// defaults it to true, so a role document written before the field existed reads as true.
        /// </summary>
        public static bool UsesHierarchy(IEnumerable<Role>? organizationRoles) =>
            (organizationRoles ?? []).Any(r => r is not null && !r.IsArchived && !string.IsNullOrWhiteSpace(r.ParentRoleSlug));

        /// <summary>
        /// Membership is read the three ways an organization can be granted: the id list, or a role or
        /// permission bucket keyed by it.
        /// </summary>
        public static bool IsMember(AccessSubject subject, string organizationId) =>
            !string.IsNullOrWhiteSpace(organizationId)
            && (subject.OrganizationIds.Contains(organizationId, StringComparer.Ordinal)
                || subject.Roles.ContainsKey(organizationId)
                || subject.Permissions.ContainsKey(organizationId));

        /// <summary>How far <paramref name="caller"/> reaches <paramref name="target"/> in the caller's organization.</summary>
        public static UserAccessLevel Evaluate(CallerAccess caller, AccessSubject target)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(target);

            switch (caller.Kind)
            {
                case CallerKind.Denied:
                    return UserAccessLevel.None;
                case CallerKind.System:
                case CallerKind.Supreme:
                    return UserAccessLevel.Manage;
            }

            // A single-organization tenant has one organization, so every account belongs to it --
            // including ones stored before memberships were written down.
            if (caller.IsMultiOrgEnabled && !IsMember(target, caller.OrganizationId))
            {
                return UserAccessLevel.None;
            }

            if (caller.Kind == CallerKind.Machine || !caller.UsesHierarchy || caller.IsTopRoleHolder)
            {
                return UserAccessLevel.Manage;
            }

            if (Matches(caller, target.ManageListFor(caller.OrganizationId)))
            {
                return UserAccessLevel.Manage;
            }

            return Matches(caller, target.AllowedToView.GetValueOrDefault(caller.OrganizationId))
                ? UserAccessLevel.View
                : UserAccessLevel.None;
        }

        /// <summary>
        /// True when the list names the caller, one of its roles or one of its effective permissions.
        /// A blank caller id never matches, so a machine token cannot be mistaken for a listed user.
        /// </summary>
        public static bool Matches(CallerAccess caller, UserAccessList? list)
        {
            if (list is null)
            {
                return false;
            }

            return (!string.IsNullOrWhiteSpace(caller.UserId)
                    && (list.Users ?? []).Contains(caller.UserId, StringComparer.Ordinal))
                || (list.Roles ?? []).Any(caller.Roles.Contains)
                || (list.Permissions ?? []).Any(caller.EffectivePermissions.Contains);
        }

        /// <summary>The IAM permission that lets a caller change users at all.</summary>
        public const string MutateUsersPermission = "blocks-iam::iam::mutate-users";

        /// <summary>
        /// Whether the caller holds <paramref name="permission"/>. Supreme and system callers are not
        /// resolved against this tenant's permissions (an impersonating owner's live in their own
        /// tenant), and reached the endpoint through its own gate, so they are taken to hold it.
        /// </summary>
        public static bool Holds(CallerAccess caller, string permission) =>
            caller.Kind is CallerKind.Supreme or CallerKind.System
            || (caller.Kind is (CallerKind.User or CallerKind.Machine) && caller.EffectivePermissions.Contains(permission));

        /// <summary>What a response may promise: the caller can both reach the user and change users at all.</summary>
        public static bool CanManage(CallerAccess caller, AccessSubject target) =>
            Evaluate(caller, target) == UserAccessLevel.Manage && Holds(caller, MutateUsersPermission);

        /// <summary>
        /// Whether the lists need consulting for this caller at all. When false, the organization
        /// scope alone decides and a list query would only cost time.
        /// </summary>
        public static bool RequiresAccessListFilter(CallerAccess caller) =>
            caller.Kind == CallerKind.User && caller.UsesHierarchy && !caller.IsTopRoleHolder;

        /// <summary>
        /// The manage list a user starts with when <paramref name="adderUserId"/> places them in an
        /// organization: the adder, and every role above the adder's roles. Top roles are left out
        /// because their holders already reach everyone.
        /// </summary>
        /// <remarks>
        /// Peers are deliberately excluded. A lawyer's client is managed by that lawyer and by the
        /// roles above lawyer, not by the other lawyers -- sharing with a peer is an explicit list edit.
        /// </remarks>
        public static UserAccessList InitialManageList(
            string? adderUserId,
            IEnumerable<string>? adderRoles,
            IEnumerable<Role>? organizationRoles,
            string targetUserId)
        {
            var list = new UserAccessList();

            if (!string.IsNullOrWhiteSpace(adderUserId) && !string.Equals(adderUserId, targetUserId, StringComparison.Ordinal))
            {
                list.Users.Add(adderUserId);
            }

            var roles = (organizationRoles ?? []).Where(r => r is not null && !r.IsArchived).ToList();
            var bySlug = roles
                .GroupBy(r => r.Slug, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var topSlugs = TopRoles(roles).Select(r => r.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var adderRole in adderRoles ?? [])
            {
                if (string.IsNullOrWhiteSpace(adderRole) || !bySlug.TryGetValue(adderRole, out var role))
                {
                    continue;
                }

                foreach (var ancestor in role.AncestorRoleSlugs ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(ancestor)
                        && bySlug.ContainsKey(ancestor)
                        && !topSlugs.Contains(ancestor)
                        && !list.Roles.Contains(ancestor, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Roles.Add(ancestor.ToLowerInvariant());
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// A role outside the hierarchy: no parent, no ancestors, cannot create roles, and nothing
        /// hangs below it.
        /// </summary>
        public static bool IsStandalone(Role role, IEnumerable<Role> organizationRoles)
        {
            if (role.CanCreateOwn
                || !string.IsNullOrWhiteSpace(role.ParentRoleSlug)
                || (role.AncestorRoleSlugs?.Count ?? 0) > 0)
            {
                return false;
            }

            return !organizationRoles.Any(other =>
                string.Equals(other.ParentRoleSlug, role.Slug, StringComparison.OrdinalIgnoreCase)
                || (other.AncestorRoleSlugs ?? []).Contains(role.Slug, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Whether <paramref name="caller"/> may grant (or take away) <paramref name="role"/>.
        /// </summary>
        /// <param name="rolePermissions">
        /// The permissions the role carries in the organization. Read only for standalone roles, where
        /// it is the whole rule: a caller may hand out a role outside the hierarchy only if every
        /// permission it carries is one the caller already has -- nobody grants more than they hold.
        /// </param>
        public static bool CanGrant(CallerAccess caller, Role role, IReadOnlySet<string>? rolePermissions)
        {
            if (caller.Kind == CallerKind.Denied || role is null || role.IsArchived)
            {
                return false;
            }

            if (caller.BypassesHierarchy)
            {
                return true;
            }

            // The existing CanCreateOwn delegation: a role that may create below it grants itself and
            // everything under it.
            var creatable = caller.OrganizationRoles
                .Where(r => r.CanCreateOwn && caller.Roles.Contains(r.Slug))
                .Select(r => r.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (creatable.Contains(role.Slug) || (role.AncestorRoleSlugs ?? []).Any(creatable.Contains))
            {
                return true;
            }

            // Without a hierarchy a standalone role is grantable by anyone, as it always was.
            return IsStandalone(role, caller.OrganizationRoles)
                && (!caller.UsesHierarchy
                    || (rolePermissions ?? new HashSet<string>()).All(caller.EffectivePermissions.Contains));
        }

        /// <summary>
        /// First requested role the caller may not grant, or null when all are grantable. A blank or
        /// unknown slug is reported rather than skipped: storing a role that does not exist is how an
        /// assignment outlives the rules that should govern it.
        /// </summary>
        public static string? FindUngrantableRole(
            CallerAccess caller,
            IEnumerable<string>? requestedRoles,
            IReadOnlyDictionary<string, IReadOnlySet<string>> permissionsByRole)
        {
            var bySlug = caller.OrganizationRoles
                .GroupBy(r => r.Slug, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var slug in requestedRoles ?? [])
            {
                if (string.IsNullOrWhiteSpace(slug) || !bySlug.TryGetValue(slug, out var role))
                {
                    return slug ?? string.Empty;
                }

                if (!CanGrant(caller, role, permissionsByRole.GetValueOrDefault(role.Slug)))
                {
                    return slug;
                }
            }

            return null;
        }

        /// <summary>
        /// First requested permission the caller may not grant, or null. A caller bound by the
        /// hierarchy must hold it (which also proves it exists); one that bypasses the hierarchy holds
        /// nothing here to compare against, so for it the permission must exist in the organization.
        /// </summary>
        public static string? FindUngrantablePermission(
            CallerAccess caller,
            IEnumerable<string>? requestedPermissions,
            IReadOnlySet<string> organizationPermissions)
        {
            foreach (var permission in requestedPermissions ?? [])
            {
                if (string.IsNullOrWhiteSpace(permission)
                    || (caller.BypassesHierarchy && !organizationPermissions.Contains(permission)))
                {
                    return permission ?? string.Empty;
                }

                // Held directly, as before, unless the organization uses the hierarchy -- then a
                // permission that comes with one of the caller's roles counts too.
                var held = caller.UsesHierarchy ? caller.EffectivePermissions : caller.DirectPermissions;
                if (caller.Kind == CallerKind.Denied
                    || (!caller.BypassesHierarchy && !held.Contains(permission)))
                {
                    return permission;
                }
            }

            return null;
        }
    }
}
