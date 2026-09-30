using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Users
{
    public class UserManagementQueryService : IUserManagementQueryService
    {
        private readonly ILogger<UserManagementQueryService> _logger;
        private readonly IUserRepository _userRepository;
        private readonly TimeProvider _timeProvider;
        private readonly IIdentityAccessManagementRepository? _identityAccessManagementRepository;
        private readonly IAccessPolicyService? _accessPolicy;

        public UserManagementQueryService(
            ILogger<UserManagementQueryService> logger,
            IUserRepository userRepository,
            // Optional with a System fallback rather than a DI registration: this service is
            // registered from Authentication.DomainService's RegisterAllServices (the root the API
            // actually calls), so a required dependency registered elsewhere would be unresolvable
            // at runtime while every unit test still passed.
            TimeProvider? timeProvider = null,
            IIdentityAccessManagementRepository? identityAccessManagementRepository = null,
            IAccessPolicyService? accessPolicy = null
        )
        {
            _logger = logger;
            _userRepository = userRepository;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _identityAccessManagementRepository = identityAccessManagementRepository;
            _accessPolicy = accessPolicy;
        }

        /// <summary>
        /// The caller for <paramref name="organizationId"/>. Without the policy service (a composition
        /// that predates it) the organization scope is still enforced from the token, but no access
        /// list or role data is available, so the answer is the pre-policy one: every member of the
        /// caller's organization, all fields.
        /// </summary>
        private async Task<(CallerAccess Caller, bool Legacy)> ResolveCallerAsync(string? organizationId)
        {
            if (_accessPolicy is not null)
            {
                return (await _accessPolicy.ResolveCallerAsync(organizationId), false);
            }

            var context = BlocksContext.GetContext();
            var target = string.IsNullOrWhiteSpace(organizationId) ? null : organizationId.Trim();

            if (context is null || !context.IsAuthenticated)
            {
                return (new CallerAccess { Kind = CallerKind.System, OrganizationId = target ?? IdpConstants.DefaultOrganizationId }, true);
            }

            var isMultiOrg = _identityAccessManagementRepository is not null
                && MultiOrgMode.IsEnabled(await _identityAccessManagementRepository.GetTenantConfigurationAsync(), _logger);
            var tokenOrg = context.OrganizationId?.Trim();

            if (context.Impersonated)
            {
                return (new CallerAccess { Kind = CallerKind.Supreme, OrganizationId = target ?? tokenOrg ?? IdpConstants.DefaultOrganizationId }, true);
            }

            if (string.IsNullOrWhiteSpace(tokenOrg) || tokenOrg == IdpConstants.NoOrganizationId)
            {
                return (CallerAccess.Denied, true);
            }

            if (isMultiOrg && tokenOrg == IdpConstants.DefaultOrganizationId)
            {
                return (new CallerAccess { Kind = CallerKind.Supreme, OrganizationId = target ?? tokenOrg, IsMultiOrgEnabled = true }, true);
            }

            if (target is not null && target != tokenOrg)
            {
                return (CallerAccess.Denied, true);
            }

            return (new CallerAccess { Kind = CallerKind.User, OrganizationId = tokenOrg, UserId = context.UserId ?? string.Empty, IsMultiOrgEnabled = isMultiOrg }, true);
        }

        public async Task<bool> IsUserAvailableAsync(IsEmailAvailableRequest query)
        {
            _logger.LogInformation("User existance search start");

            var user = await _userRepository.GetUserByEmailAsync(query.Email);

            _logger.LogInformation("User existance search end");

            return user == null;
        }

        public async Task<IsUserExistResponse> IsUserExistAsync(string email)
        {
            var user = await _userRepository.GetUserByEmailAsync(email);

            return new IsUserExistResponse
            {
                UserId = user?.ItemId,
                OrganizationIds = user?.OrganizationIds ?? new List<string>()
            };
        }

        public async Task<GetUsersResponse> GetUsersAsync(GetUsersRequest query)
        {
            _logger.LogInformation("User get start");

            query.Filter ??= new GetUsersFilter();

            var scope = UserListOrganizationScope.Resolve(
                BlocksContext.GetContext()?.OrganizationId,
                query.Filter.OrganizationIds);

            // A token that names no organization is answered without touching the database at all:
            // there is no organization to scope to, so there is nothing it may be shown.
            if (scope.Kind == UserListScopeKind.Denied)
            {
                _logger.LogInformation("User get end -- denied, the token carries no organization");

                return new GetUsersResponse
                {
                    Data = Enumerable.Empty<Dictionary<string, object>>().AsQueryable(),
                    TotalCount = 0
                };
            }

            if (scope.Kind == UserListScopeKind.AllOrganizations && query.Filter.Roles is { Count: > 0 })
            {
                if (_identityAccessManagementRepository is null)
                {
                    throw new InvalidOperationException(
                        $"{nameof(IIdentityAccessManagementRepository)} is required to filter users by role from an all-organizations scope.");
                }

                var tenantConfig = await _identityAccessManagementRepository.GetTenantConfigurationAsync();
                if (MultiOrgMode.IsEnabled(tenantConfig, _logger))
                {
                    _logger.LogInformation("User get end -- organization ids are required to filter users by role in multi-org mode");

                    return new GetUsersResponse
                    {
                        Data = Enumerable.Empty<Dictionary<string, object>>().AsQueryable(),
                        TotalCount = 0,
                        Errors = new Dictionary<string, string>
                        {
                            ["OrganizationIds"] = "OrganizationIds_Required_For_Role_Filter"
                        }
                    };
                }

                scope = new UserListScope(UserListScopeKind.Organizations, [IdpConstants.DefaultOrganizationId]);
            }

            // A caller bound to one organization is resolved for it; one reaching several (the
            // tenant-wide scope) is resolved for none in particular and is supreme over all of them.
            // Without the policy service a tenant-wide scope needs no further resolution, and must not
            // cost a configuration read.
            var (caller, legacy) = _accessPolicy is null && scope.Kind == UserListScopeKind.AllOrganizations
                ? (new CallerAccess { Kind = CallerKind.Supreme, OrganizationId = IdpConstants.DefaultOrganizationId }, true)
                : await ResolveCallerAsync(
                    scope.Kind == UserListScopeKind.Organizations && scope.OrganizationIds.Count == 1
                        ? scope.OrganizationIds[0]
                        : null);

            if (caller.Kind == CallerKind.Denied)
            {
                _logger.LogInformation("User get end -- denied, the organization is outside the token's scope");

                return new GetUsersResponse
                {
                    Data = Enumerable.Empty<Dictionary<string, object>>().AsQueryable(),
                    TotalCount = 0
                };
            }

            if (UserAccessPolicy.RequiresAccessListFilter(caller))
            {
                scope = scope with { Access = UserAccessFilter.For(caller, manageOnly: false) };
            }

            var (data, count) = await _userRepository.GetUsersAsync<GetAccounts, GetUsersRequest>(query, scope);

            // One instant for the whole response, per the data contract's "server UTC time at
            // response construction" - so every item in a page is judged against the same moment.
            // Lazy so an empty page never reads the clock at all: C2 says no lockout computation is
            // attempted when nothing matches, and a test with a throwing clock proves it.
            var asOfUtc = new Lazy<DateTime>(() => _timeProvider.GetUtcNow().UtcDateTime);

            // Only the organizations this response is about. The whole dictionary used to be
            // returned, telling one organization which others its members belong to and with what.
            IReadOnlyCollection<string>? visibleOrganizations = scope.Kind == UserListScopeKind.Organizations
                ? scope.OrganizationIds
                : null;

            var selectedUsers = data?.AsEnumerable().Select(user =>
            {
                var canManage = legacy || UserAccessPolicy.CanManage(caller, AccessSubject.From(user));
                return MapToListAccountFields(user, asOfUtc.Value, visibleOrganizations, canManage, includeSensitive: canManage || !caller.UsesHierarchy);
            })
            .Where(user => user.Count > 0).AsQueryable() ?? Enumerable.Empty<Dictionary<string, object>>().AsQueryable();

            _logger.LogInformation("User get end");

            return new GetUsersResponse
            {
                Data = selectedUsers,
                TotalCount = count
            };
        }

        public async Task<GetUserResponse> GetAccountAsync()
        {
            _logger.LogInformation("User get start");

            var bc = BlocksContext.GetContext();
            var user = await _userRepository.GetUserByIdAsync<GetAccounts>(bc?.UserId);
            var contextOrgId = string.IsNullOrWhiteSpace(bc?.OrganizationId) ? "default" : bc.OrganizationId;

            var data = user == null ? null : MapToSingleAccountFields(user, contextOrgId);

            _logger.LogInformation("User get end");

            return new GetUserResponse
            {
                Data = data
            };
        }

        public async Task<GetUserResponse> GetUserAsync(string id, string? organizationId)
        {
            _logger.LogInformation("User get start");

            var bc = BlocksContext.GetContext();
            var userId = string.IsNullOrWhiteSpace(id) ? (bc?.UserId ?? string.Empty) : id;
            var user = await _userRepository.GetUserByIdAsync<GetAccounts>(userId);

            // The organization comes from the token. The query value is honoured only for a caller
            // above every organization; for anyone else naming another organization -- "default"
            // included, which used to unlock every membership of the user -- resolves to Denied.
            var (caller, legacy) = await ResolveCallerAsync(organizationId);

            // Read only when there is a user to map - a missing user must not trigger any lockout
            // computation (C4), and a throwing clock in the tests proves it does not.
            Dictionary<string, object>? data = null;
            if (user != null)
            {
                var subject = AccessSubject.From(user);
                var level = UserAccessPolicy.Evaluate(caller, subject);

                data = level == UserAccessLevel.None
                    ? new Dictionary<string, object>()
                    : MapToSingleUserFields(
                        user,
                        caller.OrganizationId,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        includeSensitive: legacy || !caller.UsesHierarchy || UserAccessPolicy.CanManage(caller, subject),
                        isSupreme: caller.Kind is CallerKind.Supreme or CallerKind.System,
                        skipMembershipCheck: caller.Kind is CallerKind.Supreme or CallerKind.System || !caller.IsMultiOrgEnabled);

                if (data.Count > 0)
                {
                    var canManage = legacy || UserAccessPolicy.CanManage(caller, subject);
                    data["canManage"] = canManage;

                    // Who else reaches this user is shown only to those who may change it.
                    if (canManage)
                    {
                        data["allowedToView"] = user.AllowedToView.GetValueOrDefault(caller.OrganizationId) ?? new UserAccessList();
                        data["allowedToManage"] = user.AllowedToManage.GetValueOrDefault(caller.OrganizationId) ?? new UserAccessList();
                    }

                    // As before for the "default" organization: above every organization, or the one
                    // organization of a single-organization tenant.
                    if (caller.Kind is CallerKind.Supreme or CallerKind.System
                        || (!caller.IsMultiOrgEnabled && caller.OrganizationId == IdpConstants.DefaultOrganizationId))
                    {
                        data["OrganizationsRoles"] = user.Roles;
                        data["OrganizationsPermissions"] = user.Permissions;
                    }
                }
            }

            _logger.LogInformation("User get end");

            return new GetUserResponse
            {
                Data = data
            };
        }

        /// <param name="visibleOrganizations">The organizations whose roles may be shown; null for every organization.</param>
        /// <param name="canManage">
        /// Security posture and activity (MFA, lockout, sign-in history) are shown only to a caller who
        /// can manage the user: to anyone else they are a map of the weakest accounts and a record of
        /// someone's habits.
        /// </param>
        private static Dictionary<string, object> MapToListAccountFields(
            GetAccounts user,
            DateTime asOfUtc,
            IReadOnlyCollection<string>? visibleOrganizations = null,
            bool canManage = true,
            bool includeSensitive = true)
        {
            var roles = visibleOrganizations is null
                ? user.Roles
                : user.Roles
                    .Where(kv => visibleOrganizations.Contains(kv.Key, StringComparer.Ordinal))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

            var fields = new Dictionary<string, object>
            {
                ["itemId"] = user.ItemId,
                ["firstName"] = user.FirstName ?? string.Empty,
                ["lastName"] = user.LastName ?? string.Empty,
                ["email"] = user.Email,
                ["userName"] = user.UserName ?? string.Empty,
                ["active"] = user.Active,
                ["status"] = user.Status,
                ["isVerified"] = user.IsVerified,
                ["profileImageUrl"] = user.ProfileImageUrl ?? string.Empty,
                ["createdDate"] = user.CreatedDate,
                ["roles"] = roles,
                ["canManage"] = canManage
            };

            if (includeSensitive)
            {
                fields["mfaEnabled"] = user.MfaEnabled;
                fields["lastLoggedInTime"] = user.LastLoggedInTime;
                fields["loginCount"] = user.LogInCount;
                fields["lockoutUntilUtc"] = user.LockoutUntilUtc;
                fields["isLockedOut"] = IsLockedOut(user.LockoutUntilUtc, asOfUtc);
            }

            return fields;
        }

        private static Dictionary<string, object> MapToSingleAccountFields(GetAccounts user, string contextOrgId)
        {
            if (!user.OrganizationIds.Contains(contextOrgId))
            {
                return new Dictionary<string, object>();
            }

            return new Dictionary<string, object>
            {
                ["itemId"] = user.ItemId,
                ["createdDate"] = user.CreatedDate,
                ["lastUpdatedDate"] = user.LastUpdatedDate,
                ["language"] = user.Language ?? string.Empty,
                ["salutation"] = user.Salutation ?? string.Empty,
                ["firstName"] = user.FirstName ?? string.Empty,
                ["lastName"] = user.LastName ?? string.Empty,
                ["email"] = user.Email,
                ["phoneNumber"] = user.PhoneNumber ?? string.Empty,
                ["roles"] = user.Roles.ContainsKey(contextOrgId) ? user.Roles[contextOrgId] : new List<string>(),
                ["permissions"] = user.Permissions.ContainsKey(contextOrgId) ? user.Permissions[contextOrgId] : new List<string>(),
                ["active"] = user.Active,
                ["status"] = user.Status,
                ["isVerified"] = user.IsVerified,
                ["profileImageUrl"] = user.ProfileImageUrl ?? string.Empty,
                ["mfaEnabled"] = user.MfaEnabled,
                ["isMfaVerified"] = user.IsMfaVerified,
                ["userMfaType"] = user.UserMfaType,
                ["externalIdentities"] = user.ExternalIdentities,
                ["attributes"] = user.Attributes,
                ["logInCount"] = user.LogInCount,
                ["lastLoggedInTime"] = user.LastLoggedInTime,
                ["lastLoggedInDeviceInfo"] = user.LastLoggedInDeviceInfo ?? string.Empty,
                ["organizationId"] = contextOrgId
            };
        }

        /// <remarks>
        /// The caller is expected to have decided visibility already (see <see cref="UserAccessPolicy.Evaluate"/>);
        /// the membership test here is a second guard, so a mistake upstream still cannot map a
        /// non-member. <paramref name="includeSensitive"/> gates security posture, sign-in history,
        /// linked identities and the free-form attributes.
        /// </remarks>
        private static Dictionary<string, object> MapToSingleUserFields(
            GetAccounts user,
            string contextOrgId,
            DateTime asOfUtc,
            bool includeSensitive = true,
            bool isSupreme = false,
            bool skipMembershipCheck = false)
        {
            if (!skipMembershipCheck && !UserAccessPolicy.IsMember(AccessSubject.From(user), contextOrgId))
            {
                return new Dictionary<string, object>();
            }

            var fields = new Dictionary<string, object>
            {
                ["itemId"] = user.ItemId,
                ["createdDate"] = user.CreatedDate,
                ["lastUpdatedDate"] = user.LastUpdatedDate,
                ["language"] = user.Language ?? string.Empty,
                ["salutation"] = user.Salutation ?? string.Empty,
                ["firstName"] = user.FirstName ?? string.Empty,
                ["lastName"] = user.LastName ?? string.Empty,
                ["email"] = user.Email,
                ["phoneNumber"] = user.PhoneNumber ?? string.Empty,
                ["roles"] = user.Roles.ContainsKey(contextOrgId) ? user.Roles[contextOrgId] : new List<string>(),
                ["permissions"] = user.Permissions.ContainsKey(contextOrgId) ? user.Permissions[contextOrgId] : new List<string>(),
                ["active"] = user.Active,
                ["status"] = user.Status,
                ["isVerified"] = user.IsVerified,
                ["profileImageUrl"] = user.ProfileImageUrl ?? string.Empty,
                // Every membership only for a caller above all organizations; anyone else learns
                // about their own and nothing more.
                ["organizationIds"] = isSupreme
                    ? user.OrganizationIds
                    : user.OrganizationIds.Where(org => string.Equals(org, contextOrgId, StringComparison.Ordinal)).ToList()
            };

            if (includeSensitive)
            {
                fields["mfaEnabled"] = user.MfaEnabled;
                fields["isMfaVerified"] = user.IsMfaVerified;
                fields["userMfaType"] = user.UserMfaType;
                fields["externalIdentities"] = user.ExternalIdentities;
                fields["attributes"] = user.Attributes;
                fields["logInCount"] = user.LogInCount;
                fields["lastLoggedInTime"] = user.LastLoggedInTime;
                fields["lastLoggedInDeviceInfo"] = user.LastLoggedInDeviceInfo ?? string.Empty;
                // Added AFTER the cross-org early return above, so an out-of-org caller still gets
                // an empty dictionary and no lockout state leaks across organizations.
                fields["lockoutUntilUtc"] = user.LockoutUntilUtc;
                fields["isLockedOut"] = IsLockedOut(user.LockoutUntilUtc, asOfUtc);
            }

            return fields;
        }

        /// <summary>
        /// Whether the account is locked out as of <paramref name="asOfUtc"/>.
        /// </summary>
        /// <remarks>
        /// Deliberately strict &gt;, matching the check the authentication flows perform before
        /// refusing a login (e.g. PasswordAuthenticationService.cs:58), so what this API reports
        /// agrees with what actually blocks a sign-in.
        ///
        /// This is NOT a system-wide source of truth: authentication still inlines the same
        /// expression in around six places, and unifying them would mean editing authentication
        /// code paths, which is out of scope for a read-only exposure change. If that predicate ever
        /// moves, this must move with it - no test here can catch that divergence.
        /// </remarks>
        private static bool IsLockedOut(DateTime? lockoutUntilUtc, DateTime asOfUtc) =>
            lockoutUntilUtc.HasValue && lockoutUntilUtc.Value > asOfUtc;
    }
}
