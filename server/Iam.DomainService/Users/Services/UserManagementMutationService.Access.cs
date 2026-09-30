using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Utilities;

namespace Iam.DomainService.Users
{
    /// <summary>
    /// The checks the user write paths share. The rules live in <see cref="UserAccessPolicy"/>; this
    /// file only loads what they need and turns their answers into responses.
    /// </summary>
    public partial class UserManagementMutationService
    {
        private async Task<CallerAccess?> ResolveCallerAsync(string? organizationId)
        {
            return _accessPolicy is null ? null : await _accessPolicy.ResolveCallerAsync(organizationId);
        }

        private static BaseMutationResponse Failure(string key, string message) => new()
        {
            IsSuccess = false,
            Errors = new Dictionary<string, string> { { key, message } }
        };

        private static bool IsMemberOf(User user, string organizationId) =>
            UserAccessPolicy.IsMember(AccessSubject.From(user), organizationId);

        /// <summary>
        /// Refuses a write the caller may not make on <paramref name="target"/>: "not found" when the
        /// caller cannot even see the user (so existence is not disclosed), forbidden when it can see
        /// but not manage.
        /// </summary>
        private static BaseMutationResponse? ValidateManage(CallerAccess? caller, User target, string notFoundKey = "UserId")
        {
            if (caller is null)
            {
                return null;
            }

            return UserAccessPolicy.Evaluate(caller, AccessSubject.From(target)) switch
            {
                UserAccessLevel.Manage => null,
                UserAccessLevel.View => Failure("forbidden", "Not_Allowed_To_Manage_User"),
                _ => Failure(notFoundKey, "Not found")
            };
        }

        /// <summary>
        /// Refuses roles or permissions the caller may not hand out -- or take away, checked the same
        /// way, since removing a role you could never grant is how a lower administrator would demote
        /// a higher one.
        /// </summary>
        /// <param name="removal">
        /// Taking away. A slug or resource that no longer exists is then fine: removing a dangling
        /// assignment must never be refused.
        /// </param>
        private async Task<BaseMutationResponse?> ValidateGrantsAsync(
            CallerAccess? caller,
            IEnumerable<string>? roles,
            IEnumerable<string>? permissions,
            bool removal = false)
        {
            // Without a hierarchy there is nothing to rank grants against; the organization keeps its
            // existing behaviour.
            if (caller is null || _accessPolicy is null || !caller.UsesHierarchy)
            {
                return null;
            }

            if (removal)
            {
                var known = caller.OrganizationRoles.Select(r => r.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
                roles = (roles ?? []).Where(r => !string.IsNullOrWhiteSpace(r) && known.Contains(r)).ToList();

                var requested = (permissions ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                var existing = requested.Count == 0 || _resourceRepository is null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : (await _resourceRepository.GetActivePermissionsByResourcesAsync(requested, caller.OrganizationId) ?? [])
                        .Select(p => p.Resource)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                permissions = requested.Where(existing.Contains).ToList();
            }

            var deniedRole = await _accessPolicy.FindUngrantableRoleAsync(caller, roles);
            if (deniedRole != null)
            {
                return Failure("Roles", $"You cannot grant the role: {deniedRole}");
            }

            var deniedPermission = await _accessPolicy.FindUngrantablePermissionAsync(caller, permissions);
            if (deniedPermission != null)
            {
                return Failure("Permissions", $"You cannot grant the permission: {deniedPermission}");
            }

            return null;
        }

        /// <summary>
        /// Seeds <paramref name="user"/>'s manage list for an organization they are joining: whoever
        /// added them, and the roles above that person. Additive, so it never drops an entry.
        /// </summary>
        /// <remarks>
        /// Public because the signup-link redemption in the authentication assembly adds members too,
        /// on behalf of the link's creator.
        /// </remarks>
        public async Task StampInitialAccessAsync(User user, string organizationId, string? adderUserId)
        {
            ArgumentNullException.ThrowIfNull(user);

            if (string.IsNullOrWhiteSpace(organizationId) || _resourceRepository is null)
            {
                return;
            }

            IEnumerable<string> adderRoles = [];
            var adder = string.IsNullOrWhiteSpace(adderUserId) ? null : adderUserId.Trim();

            if (adder is not null)
            {
                var context = BlocksContext.GetContext();

                // The caller's token already carries its roles for this organization; anyone else (a
                // link's creator) is read, and dropped if no longer a member.
                if (context is not null
                    && string.Equals(context.UserId, adder, StringComparison.Ordinal)
                    && string.Equals(context.OrganizationId, organizationId, StringComparison.Ordinal))
                {
                    adderRoles = context.Roles ?? [];
                }
                else
                {
                    var adderUser = await _userRepository.GetUserByIdAsync(adder);
                    if (adderUser is null || !IsMemberOf(adderUser, organizationId))
                    {
                        adder = null;
                    }
                    else
                    {
                        adderRoles = adderUser.Roles.GetValueOrDefault(organizationId) ?? [];
                    }
                }
            }

            var organizationRoles = await _resourceRepository.GetRolesByOrgAsync(organizationId) ?? [];
            var seed = UserAccessPolicy.InitialManageList(adder, adderRoles, organizationRoles, user.ItemId);

            user.AllowedToManage ??= new();
            var existing = user.AllowedToManage.GetValueOrDefault(organizationId) ?? new UserAccessList();

            existing.Users = existing.Users.Union(seed.Users, StringComparer.Ordinal).ToList();
            existing.Roles = existing.Roles.Union(seed.Roles, StringComparer.OrdinalIgnoreCase).ToList();
            existing.Permissions ??= [];

            user.AllowedToManage[organizationId] = existing;
        }

        /// <summary>
        /// Deactivation and reactivation act on the whole account, not one membership. So beyond
        /// managing the user, an organization-bound caller may do it only when the account belongs to
        /// its organization alone, and only with the authority to grant every role the user holds.
        /// </summary>
        private async Task<BaseMutationResponse?> ValidateAccountWideChangeAsync(User target, string notFoundKey)
        {
            var caller = await ResolveCallerAsync(null);
            if (caller is null)
            {
                return null;
            }

            if (caller.Kind == CallerKind.Denied)
            {
                return Failure("OrganizationId", "Other org user can not add/update");
            }

            if (caller.UsesHierarchy
                && !string.IsNullOrWhiteSpace(caller.UserId)
                && string.Equals(caller.UserId, target.ItemId, StringComparison.Ordinal))
            {
                return Failure("UserId", "You cannot change the status of your own account");
            }

            var manageFailure = ValidateManage(caller, target, notFoundKey);
            if (manageFailure != null)
            {
                return manageFailure;
            }

            var organizationId = caller.OrganizationId;

            if (caller.Kind is CallerKind.User or CallerKind.Machine)
            {
                var otherOrganizations = target.OrganizationIds
                    .Concat(target.Roles.Keys)
                    .Concat(target.Permissions.Keys)
                    .Any(org => !string.IsNullOrWhiteSpace(org) && !string.Equals(org, organizationId, StringComparison.Ordinal));

                if (otherOrganizations)
                {
                    return Failure("forbidden", "User_Belongs_To_Other_Organizations");
                }
            }

            return await ValidateGrantsAsync(
                caller,
                target.Roles.GetValueOrDefault(organizationId) ?? [],
                target.Permissions.GetValueOrDefault(organizationId) ?? [],
                removal: true);
        }

        /// <summary>Drops a user's own lists for an organization they are leaving.</summary>
        private static void ClearOwnAccessLists(User user, string organizationId)
        {
            user.AllowedToView?.Remove(organizationId);
            user.AllowedToManage?.Remove(organizationId);
        }

        /// <summary>
        /// Replaces <paramref name="target"/>'s lists for the organization with the requested ones, in
        /// memory -- the caller saves. A null list is left as it is.
        /// </summary>
        /// <remarks>
        /// Anyone who may manage the user may list people. Role and permission entries open the user
        /// to a whole group, so only the top role (or a caller above the hierarchy) may change them.
        /// </remarks>
        private static BaseMutationResponse? ApplyAccessLists(
            CallerAccess? caller,
            User target,
            string organizationId,
            UserAccessList? requestedView,
            UserAccessList? requestedManage)
        {
            if (caller is null || (requestedView is null && requestedManage is null))
            {
                return null;
            }

            target.AllowedToView ??= new();
            target.AllowedToManage ??= new();
            var currentView = target.AllowedToView.GetValueOrDefault(organizationId) ?? new UserAccessList();
            var currentManage = target.AllowedToManage.GetValueOrDefault(organizationId) ?? new UserAccessList();

            var manage = Normalize(requestedManage ?? currentManage, target.ItemId);
            var view = Normalize(requestedView ?? currentView, target.ItemId);

            // Managing includes viewing, so an entry is kept once, in the stronger list.
            view.Users = view.Users.Except(manage.Users, StringComparer.Ordinal).ToList();
            view.Roles = view.Roles.Except(manage.Roles, StringComparer.OrdinalIgnoreCase).ToList();
            view.Permissions = view.Permissions.Except(manage.Permissions, StringComparer.OrdinalIgnoreCase).ToList();

            var groupsChanged =
                !SameSet(currentView.Roles, view.Roles) || !SameSet(currentManage.Roles, manage.Roles)
                || !SameSet(currentView.Permissions, view.Permissions) || !SameSet(currentManage.Permissions, manage.Permissions);

            if (groupsChanged && !caller.IsPrivileged)
            {
                return Failure("forbidden", "Only_Top_Role_Can_Change_Role_Or_Permission_Entries");
            }

            target.AllowedToView[organizationId] = view;
            target.AllowedToManage[organizationId] = manage;
            return null;

            // Blank and duplicate entries dropped, and the user never listed on their own account.
            static UserAccessList Normalize(UserAccessList list, string targetId) => new()
            {
                Users = (list.Users ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())
                    .Where(v => !string.Equals(v, targetId, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList(),
                Roles = (list.Roles ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Permissions = (list.Permissions ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };

            static bool SameSet(List<string>? a, List<string>? b) =>
                (a ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(b ?? []);
        }
    }
}
