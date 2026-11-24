using System.Text.Json;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Shared.Serialization;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using MongoDB.Driver;

namespace Iam.DomainService.Batch
{
    /// <summary>
    /// The job's submitter as the worker sees it, rebuilt from the job when it runs, never from the
    /// message context.
    /// </summary>
    /// <param name="IsSystem">The submitter was the system user (default organization or platform).</param>
    /// <param name="OrganizationId">The submitter's organization.</param>
    /// <param name="Scope">The submitter's grant scope in its organization; null for the system user (no grant rule applies).</param>
    /// <param name="OwnPermissions">
    /// The permissions the submitter holds in its organization (its roles' and its direct ones). Null only for a
    /// platform call with no user and no client, which needs no per-user permission.
    /// </param>
    public sealed record BatchCaller(
        bool IsSystem,
        string OrganizationId,
        GrantScope? Scope,
        IReadOnlySet<string>? OwnPermissions)
    {
        /// <summary>True when the submitter may act anywhere in the tenant (H7).</summary>
        public bool IsTenantWide => IsSystem || OrganizationId == IdpConstants.DefaultOrganizationId;

        public bool Holds(string permission) => OwnPermissions is null || OwnPermissions.Contains(permission);
    }

    /// <summary>Everything a job's validation reads, loaded in bulk before any item is looked at.</summary>
    public sealed record BatchPreload(
        bool MultiOrgEnabled,
        IReadOnlyDictionary<string, Organization> Organizations,
        IReadOnlyDictionary<string, IReadOnlySet<string>> LiveRoles,
        IReadOnlyDictionary<string, User> UsersByEmail,
        BatchCaller Caller);

    public enum BatchItemOutcome
    {
        Failed,
        Unchanged,
        Created,
        Updated
    }

    /// <summary>What one item will do: its outcome, the write and the side effects it plans.</summary>
    public sealed class BatchItemPlan
    {
        public required BatchJobItem Item { get; init; }
        public BatchItemOutcome Outcome { get; init; }
        public List<BatchItemError> Errors { get; init; } = [];

        /// <summary>The new account (Created) or the stored one (Updated).</summary>
        public User? User { get; init; }

        public UpdateDefinition<User>? Update { get; init; }
        public List<BatchPendingSideEffect> SideEffects { get; init; } = [];

        /// <summary>
        /// The organization whose template a mail to this user uses: a new user's first membership, or the first
        /// organization this job adds an existing user to. Null when the job adds the user to no organization.
        /// </summary>
        public string? MailOrganizationId { get; init; }

        public BatchItemFailure ToFailure() => new()
        {
            Ref = Item.Ref,
            Index = Item.Index,
            Email = Item.Data.Email,
            Errors = Errors
        };
    }

    /// <summary>
    /// Validates one batch item in memory against the preloaded data and plans its write (§6.5 steps 5 and 6).
    /// A user with any error is failed whole and nothing is planned for it. Writes are additive only:
    /// organizations are added and roles and permissions merged, never removed; profile fields are set when sent
    /// and different.
    /// </summary>
    public sealed class BatchUserPlanner
    {
        private const string DefaultOrganizationId = IdpConstants.DefaultOrganizationId;
        private const string ItemField = "item";

        /// <summary>The log target for a user that has no id yet; the email is kept out of the log.</summary>
        private const string NewUserTarget = "(new user)";

        private readonly IAccessGrantService _accessGrant;
        private readonly Func<CreateUserRequest, User> _mapUser;

        public BatchUserPlanner(IAccessGrantService accessGrant, Func<CreateUserRequest, User> mapUser)
        {
            _accessGrant = accessGrant;
            _mapUser = mapUser;
        }

        /// <summary>One membership as it will be written.</summary>
        private sealed record PlannedMembership(
            int Index,
            string OrganizationId,
            bool IsMember,
            List<string> Roles,
            List<string> Permissions,
            List<string> AddedRoles,
            List<string> AddedPermissions)
        {
            public bool Adds => !IsMember || AddedRoles.Count > 0 || AddedPermissions.Count > 0;
        }

        public async Task<BatchItemPlan> PlanAsync(BatchJob job, BatchJobItem item, BatchPreload preload)
        {
            var data = item.Data;
            preload.UsersByEmail.TryGetValue(data.Email ?? string.Empty, out var existing);

            var errors = new List<BatchItemError>();
            var memberships = ResolveMemberships(data, existing, preload, errors);
            if (errors.Count > 0)
            {
                return Failed(item, errors);
            }

            var changedFields = existing is null ? new List<string>() : ChangedProfileFields(existing, data);
            var denial = existing is null
                ? await CheckNewUserAsync(data, memberships, preload.Caller)
                : await CheckExistingUserAsync(existing, data, memberships, changedFields.Count > 0, preload.Caller);
            if (denial is not null)
            {
                return Failed(item, [denial]);
            }

            var actor = job.RequestedBy ?? job.RequestedByClientId;
            return existing is null
                ? PlanCreate(item, memberships, actor)
                : PlanUpdate(item, existing, memberships, changedFields, actor);
        }

        private static BatchItemPlan Failed(BatchJobItem item, List<BatchItemError> errors) =>
            new() { Item = item, Outcome = BatchItemOutcome.Failed, Errors = errors };

        #region Memberships

        private static List<PlannedMembership> ResolveMemberships(
            BatchUserData data, User? existing, BatchPreload preload, List<BatchItemError> errors)
        {
            var requested = data.Memberships ?? [];
            if (requested.Count == 0 && existing is null)
            {
                if (preload.MultiOrgEnabled)
                {
                    errors.Add(BatchErrorCodes.Error("memberships", BatchErrorCodes.OrganizationRequired));
                    return [];
                }

                // A single-organization tenant places every new user in the default organization, as
                // users/create (SaveNewUserAsync) does.
                requested = [new BatchMembership { OrganizationId = DefaultOrganizationId }];
            }

            var planned = new List<PlannedMembership>();
            for (var i = 0; i < requested.Count; i++)
            {
                var organizationId = ResolveOrganization(requested[i], i, preload, errors);
                if (organizationId is null || !RolesExist(requested[i], i, organizationId, preload, errors))
                {
                    continue;
                }

                // Blank, null and "default" all resolve to the default organization, so the request-level
                // ordinal check cannot see that two of them name the same one.
                if (planned.Any(m => m.OrganizationId == organizationId))
                {
                    errors.Add(BatchErrorCodes.Error($"memberships[{i}].organizationId", BatchErrorCodes.OrganizationDuplicate));
                    continue;
                }

                var membership = Plan(i, organizationId, requested[i], existing, preload);
                var permissionCount = AccessGrantRules.Clean(GrantsOf(existing?.Permissions, organizationId).Concat(membership.AddedPermissions)).Count();
                if (permissionCount > UserIdentityRules.MaxPermissionsPerUser)
                {
                    errors.Add(BatchErrorCodes.Error($"memberships[{i}].permissions", BatchErrorCodes.TooManyPermissions));
                    continue;
                }

                planned.Add(membership);
            }

            return planned;
        }

        /// <summary>The organization a membership names, or null with its error.</summary>
        private static string? ResolveOrganization(BatchMembership membership, int index, BatchPreload preload, List<BatchItemError> errors)
        {
            var field = $"memberships[{index}].organizationId";
            var code = OrganizationError(membership.OrganizationId, preload);
            if (code is not null)
            {
                errors.Add(BatchErrorCodes.Error(field, code));
                return null;
            }

            var organizationId = preload.MultiOrgEnabled ? membership.OrganizationId! : DefaultOrganizationId;

            // The organization rule is taken from the job's submitter, not from BlocksContext: the
            // worker's context is not the caller's (C6).
            if (!preload.Caller.IsTenantWide && !string.Equals(organizationId, preload.Caller.OrganizationId, StringComparison.Ordinal))
            {
                errors.Add(BatchErrorCodes.Error(field, BatchErrorCodes.OrganizationNotAllowed));
                return null;
            }

            return organizationId;
        }

        private static string? OrganizationError(string? organizationId, BatchPreload preload)
        {
            var isDefault = string.IsNullOrWhiteSpace(organizationId) || organizationId == DefaultOrganizationId;
            if (!preload.MultiOrgEnabled)
            {
                return isDefault ? null : BatchErrorCodes.OrganizationNotSupported;
            }

            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return BatchErrorCodes.OrganizationRequired;
            }

            if (isDefault)
            {
                return null;
            }

            if (!preload.Organizations.TryGetValue(organizationId, out var organization))
            {
                return BatchErrorCodes.OrganizationNotFound;
            }

            return organization.IsDisabled ? BatchErrorCodes.OrganizationDisabled : null;
        }

        private static bool RolesExist(BatchMembership membership, int index, string organizationId, BatchPreload preload, List<BatchItemError> errors)
        {
            var live = preload.LiveRoles.GetValueOrDefault(organizationId);
            var roles = membership.Roles ?? [];
            var valid = true;
            for (var j = 0; j < roles.Count; j++)
            {
                if (live is null || !live.Contains(roles[j]))
                {
                    errors.Add(BatchErrorCodes.Error($"memberships[{index}].roles[{j}]", BatchErrorCodes.RoleNotFound));
                    valid = false;
                }
            }

            return valid;
        }

        private static PlannedMembership Plan(int index, string organizationId, BatchMembership membership, User? existing, BatchPreload preload)
        {
            var isMember = existing is not null && AccessGrantRules.IsMember(existing, organizationId);
            var roles = membership.Roles is { Count: > 0 } ? membership.Roles : [];

            // A new membership without roles gets the organization's member defaults, as
            // ApplyOrganizationDefaultsAsync gives them on users/create. Permissions are not defaulted.
            if (roles.Count == 0 && !isMember && preload.Organizations.TryGetValue(organizationId, out var organization))
            {
                roles = organization.DefaultRoleForMembers ?? [];
            }

            var permissions = membership.Permissions ?? [];
            var storedRoles = GrantsOf(existing?.Roles, organizationId);
            var storedPermissions = GrantsOf(existing?.Permissions, organizationId);
            return new PlannedMembership(
                index,
                organizationId,
                isMember,
                AccessGrantRules.Clean(roles).ToList(),
                AccessGrantRules.Clean(permissions).ToList(),
                AccessGrantRules.Delta(storedRoles, roles).Added,
                AccessGrantRules.Delta(storedPermissions, permissions).Added);
        }

        private static List<string> GrantsOf(Dictionary<string, List<string>>? grants, string organizationId) =>
            AccessGrantRules.GrantsIn(grants, organizationId).ToList();

        #endregion

        #region Grant checks

        /// <summary>R10 on the attributes and R1/R2 on each membership, in the submitter's scope (system: none).</summary>
        private async Task<BatchItemError?> CheckNewUserAsync(BatchUserData data, List<PlannedMembership> memberships, BatchCaller caller)
        {
            var scope = caller.Scope;
            if (scope is null)
            {
                return null;
            }

            if (!_accessGrant.Authorize(scope, AccessGrantRules.CheckAccessAttributes(scope, NewUserTarget, null, data.Attributes)))
            {
                return GrantNotAllowed();
            }

            foreach (var membership in memberships)
            {
                if (!await _accessGrant.AuthorizeGrantAsync(scope, membership.Roles, membership.Permissions))
                {
                    return GrantNotAllowed();
                }
            }

            return null;
        }

        /// <summary>
        /// The users/access semantics for an existing account (A19): R4 on what is added and R3 on the account,
        /// manage-user-roles for adding to a membership it already has, update-user-profiles plus R3 and R10 for a
        /// changed profile. The submitter's own account is refused unless it is the system user (C19).
        /// </summary>
        private async Task<BatchItemError?> CheckExistingUserAsync(
            User existing, BatchUserData data, List<PlannedMembership> memberships, bool profileChanged, BatchCaller caller)
        {
            var scope = caller.Scope;
            if (scope is not null && string.Equals(existing.ItemId, scope.CallerId, StringComparison.Ordinal))
            {
                return GrantNotAllowed();
            }

            foreach (var membership in memberships.Where(m => m.Adds))
            {
                var denial = await CheckMembershipChangeAsync(existing, membership, caller);
                if (denial is not null)
                {
                    return denial;
                }
            }

            if (!profileChanged)
            {
                return null;
            }

            if (!caller.Holds(UserPermissions.UpdateUserProfiles))
            {
                return BatchErrorCodes.Error(ItemField, BatchErrorCodes.UpdateUserProfilesRequired);
            }

            return scope is null || _accessGrant.Authorize(scope,
                    AccessGrantRules.CheckManageUser(scope, existing)
                    ?? AccessGrantRules.CheckAccessAttributes(scope, existing.ItemId, existing.Attributes, data.Attributes))
                ? null
                : GrantNotAllowed();
        }

        private async Task<BatchItemError?> CheckMembershipChangeAsync(User existing, PlannedMembership membership, BatchCaller caller)
        {
            if (membership.IsMember && !caller.Holds(UserPermissions.ManageUserRoles))
            {
                return BatchErrorCodes.Error(ItemField, BatchErrorCodes.ManageUserRolesRequired);
            }

            var scope = caller.Scope;
            if (scope is null)
            {
                return null;
            }

            // The merged lists: Delta then sees exactly the added values and removes nothing.
            var roles = GrantsOf(existing.Roles, membership.OrganizationId).Concat(membership.AddedRoles).ToList();
            var permissions = GrantsOf(existing.Permissions, membership.OrganizationId).Concat(membership.AddedPermissions).ToList();
            await _accessGrant.WarnOnPolicyPowerAsync(scope, membership.AddedRoles);
            return _accessGrant.Authorize(scope, AccessGrantRules.CheckAccessChange(scope, existing, roles, permissions))
                ? null
                : GrantNotAllowed();
        }

        private static BatchItemError GrantNotAllowed() => BatchErrorCodes.Error(ItemField, BatchErrorCodes.GrantNotAllowed);

        #endregion

        #region Profile

        /// <summary>The profile fields the item sends with a value different from the stored one.</summary>
        private static List<string> ChangedProfileFields(User user, BatchUserData data)
        {
            var changed = new List<string>();
            AddIfChanged(changed, nameof(User.Salutation), data.Salutation, user.Salutation);
            AddIfChanged(changed, nameof(User.FirstName), data.FirstName, user.FirstName);
            AddIfChanged(changed, nameof(User.LastName), data.LastName, user.LastName);
            AddIfChanged(changed, nameof(User.PhoneNumber), data.PhoneNumber, user.PhoneNumber);
            AddIfChanged(changed, nameof(User.Language), data.Language, user.Language);

            if (data.Attributes is not null
                && Canonical(AttributeNormalizer.Normalize(data.Attributes, AttributePolicy.Internal))
                   != Canonical(AttributeNormalizer.Normalize(user.Attributes, AttributePolicy.Internal)))
            {
                changed.Add(nameof(User.Attributes));
            }

            return changed;
        }

        /// <summary>The bag as JSON with its keys sorted at every level, so the same values in another order compare equal.</summary>
        private static string Canonical(Dictionary<string, object> attributes) => JsonSerializer.Serialize(Sorted(attributes));

        private static object? Sorted(object? value) => value switch
        {
            IDictionary<string, object> map => new SortedDictionary<string, object?>(
                map.ToDictionary(e => e.Key, e => Sorted(e.Value)), StringComparer.Ordinal),
            string text => text,
            System.Collections.IEnumerable sequence => sequence.Cast<object?>().Select(Sorted).ToList(),
            _ => value
        };

        private static void AddIfChanged(List<string> changed, string name, string? incoming, string? current)
        {
            if (incoming is not null && !string.Equals(incoming, current, StringComparison.Ordinal))
            {
                changed.Add(name);
            }
        }

        #endregion

        #region Write plan

        private BatchItemPlan PlanCreate(BatchJobItem item, List<PlannedMembership> memberships, string? actor)
        {
            var data = item.Data;
            var first = memberships[0];
            var request = new CreateUserRequest
            {
                UserId = Guid.NewGuid().ToString(),
                Email = data.Email!,
                Salutation = data.Salutation,
                FirstName = data.FirstName,
                LastName = data.LastName,
                PhoneNumber = data.PhoneNumber,
                Attributes = data.Attributes ?? new Dictionary<string, object>(),
                OrganizationId = first.OrganizationId,
                Roles = first.Roles,
                Permissions = first.Permissions,
                UserCreationType = UserCreationType.Api,
                UserPassType = UserPassType.Password,
                VerifiedType = UserVerifiedType.None
            };
            if (data.Language is not null)
            {
                request.Language = data.Language;
            }

            var user = _mapUser(request);
            foreach (var membership in memberships.Skip(1))
            {
                if (!user.OrganizationIds.Contains(membership.OrganizationId))
                {
                    user.OrganizationIds.Add(membership.OrganizationId);
                }

                user.Roles[membership.OrganizationId] = membership.Roles;
                user.Permissions[membership.OrganizationId] = membership.Permissions;
            }

            // The worker's context is not the submitter's, so the audit fields come from the job.
            user.CreatedBy = actor ?? user.ItemId;
            user.LastUpdatedBy = actor ?? user.ItemId;

            return new BatchItemPlan
            {
                Item = item,
                Outcome = BatchItemOutcome.Created,
                User = user,
                MailOrganizationId = first.OrganizationId,
                SideEffects =
                [
                    new BatchPendingSideEffect
                    {
                        UserId = user.ItemId,
                        Kind = BatchSideEffectKinds.Created,
                        OrganizationId = first.OrganizationId
                    }
                ]
            };
        }

        private static BatchItemPlan PlanUpdate(
            BatchJobItem item, User existing, List<PlannedMembership> memberships, List<string> changedFields, string? actor)
        {
            var updates = new List<UpdateDefinition<User>>();
            var builder = Builders<User>.Update;
            var adding = memberships.Where(m => m.Adds).ToList();

            // The organization of an existing user's mail: the first one, in request order, this job adds them to.
            var firstAdded = adding.FirstOrDefault(m => !m.IsMember)?.OrganizationId;

            // An old document may hold null instead of a list or map, which $addToSet and dotted paths cannot
            // extend: such a user gets the merged membership fields written whole.
            if (adding.Count > 0 && (existing.OrganizationIds is null || existing.Roles is null || existing.Permissions is null))
            {
                updates.AddRange(WholeMembershipUpdates(existing, adding));
            }

            foreach (var membership in adding)
            {
                if (existing.OrganizationIds is not null && existing.Roles is not null && existing.Permissions is not null)
                {
                    updates.AddRange(MembershipUpdates(existing, membership));
                }
            }

            updates.AddRange(ProfileUpdates(item.Data, changedFields));
            if (updates.Count == 0)
            {
                return new BatchItemPlan { Item = item, Outcome = BatchItemOutcome.Unchanged, User = existing };
            }

            // Status and Active are never written: a deactivated account stays deactivated (C21).
            updates.Add(builder.Set(x => x.LastUpdatedDate, DateTime.UtcNow));
            updates.Add(builder.Set(x => x.LastUpdatedBy, actor ?? existing.ItemId));
            return new BatchItemPlan
            {
                Item = item,
                Outcome = BatchItemOutcome.Updated,
                User = existing,
                Update = builder.Combine(updates),
                MailOrganizationId = firstAdded
            };
        }

        /// <summary>
        /// Adds the organization and the new roles and permissions. <c>$addToSet</c> on an existing list, so a
        /// concurrent change is merged rather than overwritten; a list the user does not have yet is set.
        /// </summary>
        private static IEnumerable<UpdateDefinition<User>> MembershipUpdates(User existing, PlannedMembership membership)
        {
            var builder = Builders<User>.Update;
            var organizationId = membership.OrganizationId;
            if (!existing.OrganizationIds.Contains(organizationId))
            {
                yield return builder.AddToSet(x => x.OrganizationIds, organizationId);
            }

            var rolesField = $"{nameof(User.Roles)}.{organizationId}";
            if (!existing.Roles.ContainsKey(organizationId))
            {
                yield return builder.Set(rolesField, membership.Roles);
            }
            else if (membership.AddedRoles.Count > 0)
            {
                yield return builder.AddToSetEach(rolesField, membership.AddedRoles);
            }

            var permissionsField = $"{nameof(User.Permissions)}.{organizationId}";
            if (!existing.Permissions.ContainsKey(organizationId))
            {
                yield return builder.Set(permissionsField, membership.Permissions);
            }
            else if (membership.AddedPermissions.Count > 0)
            {
                yield return builder.AddToSetEach(permissionsField, membership.AddedPermissions);
            }
        }

        /// <summary>The union of stored and added memberships, written whole (for a document with null membership fields).</summary>
        private static IEnumerable<UpdateDefinition<User>> WholeMembershipUpdates(User existing, List<PlannedMembership> adding)
        {
            var organizationIds = (existing.OrganizationIds ?? []).ToList();
            var roles = new Dictionary<string, List<string>>(existing.Roles ?? new Dictionary<string, List<string>>());
            var permissions = new Dictionary<string, List<string>>(existing.Permissions ?? new Dictionary<string, List<string>>());
            foreach (var membership in adding)
            {
                if (!organizationIds.Contains(membership.OrganizationId))
                {
                    organizationIds.Add(membership.OrganizationId);
                }

                roles[membership.OrganizationId] = GrantsOf(roles, membership.OrganizationId).Concat(membership.AddedRoles).ToList();
                permissions[membership.OrganizationId] = GrantsOf(permissions, membership.OrganizationId).Concat(membership.AddedPermissions).ToList();
            }

            var builder = Builders<User>.Update;
            yield return builder.Set(x => x.OrganizationIds, organizationIds);
            yield return builder.Set(x => x.Roles, roles);
            yield return builder.Set(x => x.Permissions, permissions);
        }

        private static IEnumerable<UpdateDefinition<User>> ProfileUpdates(BatchUserData data, List<string> changedFields)
        {
            var builder = Builders<User>.Update;
            foreach (var field in changedFields)
            {
                yield return field switch
                {
                    nameof(User.Salutation) => builder.Set(x => x.Salutation, data.Salutation),
                    nameof(User.FirstName) => builder.Set(x => x.FirstName, data.FirstName),
                    nameof(User.LastName) => builder.Set(x => x.LastName, data.LastName),
                    nameof(User.PhoneNumber) => builder.Set(x => x.PhoneNumber, data.PhoneNumber),
                    nameof(User.Language) => builder.Set(x => x.Language, data.Language),
                    _ => builder.Set(x => x.Attributes, AttributeNormalizer.Normalize(data.Attributes, AttributePolicy.Internal))
                };
            }
        }

        #endregion
    }
}
