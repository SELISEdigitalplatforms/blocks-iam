using Blocks.Genesis;
using Iam.DomainService.Dtos;
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

        public UserManagementQueryService(
            ILogger<UserManagementQueryService> logger,
            IUserRepository userRepository,
            // Optional with a System fallback rather than a DI registration: this service is
            // registered from Authentication.DomainService's RegisterAllServices (the root the API
            // actually calls), so a required dependency registered elsewhere would be unresolvable
            // at runtime while every unit test still passed.
            TimeProvider? timeProvider = null,
            IIdentityAccessManagementRepository? identityAccessManagementRepository = null
        )
        {
            _logger = logger;
            _userRepository = userRepository;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _identityAccessManagementRepository = identityAccessManagementRepository;
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

            var (data, count) = await _userRepository.GetUsersAsync<GetAccounts, GetUsersRequest>(query, scope);

            // Skipped when nothing matched: an empty page has no dictionary to filter, so it costs
            // the same one query it always did.
            var listFields = count == 0
                ? null
                : (await _userRepository.GetIamConfigurationAsync())?.UserListFields;

            // Built once for the whole page rather than per row.
            var keep = UserFieldPolicy.BuildKeepSet(listFields, UserFieldPolicy.DefaultListFields);

            // One instant for the whole response, so every item in a page is judged against the same
            // moment. Lazy so an empty page never reads the clock at all.
            var asOfUtc = new Lazy<DateTime>(() => _timeProvider.GetUtcNow().UtcDateTime);

            var selectedUsers = data?
                .Select(user => UserFieldPolicy.Apply(MapToListAccountFields(user, asOfUtc.Value), keep))
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

            // Config is read only when there is something to filter, so a miss costs one query
            // rather than two.
            var data = user == null
                ? null
                : UserFieldPolicy.Apply(
                    MapToSingleAccountFields(user, contextOrgId),
                    (await _userRepository.GetIamConfigurationAsync())?.UserDetailFields,
                    UserFieldPolicy.DefaultDetailFields);

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
            var contextOrgId = string.IsNullOrWhiteSpace(organizationId) ? (bc?.OrganizationId ?? "default") : organizationId;

            Dictionary<string, object>? data = null;

            // Mapped only when there is a user - a missing user must not trigger any lockout
            // computation (C4), and a throwing clock in the tests proves it does not.
            if (user != null)
            {
                var fields = MapToSingleUserFields(user, contextOrgId, _timeProvider.GetUtcNow().UtcDateTime);

                // Added BEFORE the filter so these two are configurable like every other key, and
                // guarded on Count because a caller outside the user's organizations gets an empty
                // dictionary from the mapper and must not gain keys here.
                if (contextOrgId == "default" && fields.Count > 0)
                {
                    fields["OrganizationsRoles"] = user.Roles;
                    fields["OrganizationsPermissions"] = user.Permissions;
                }

                data = UserFieldPolicy.Apply(
                    fields,
                    (await _userRepository.GetIamConfigurationAsync())?.UserDetailFields,
                    UserFieldPolicy.DefaultDetailFields);
            }

            _logger.LogInformation("User get end");

            return new GetUserResponse
            {
                Data = data
            };
        }

        /// <summary>
        /// Everything the list CAN return. What it actually returns is this narrowed by
        /// <see cref="UserFieldPolicy"/> -- by default to <see cref="UserFieldPolicy.DefaultListFields"/>,
        /// or to whatever the tenant configured. Keys are built here so they are available to
        /// configure; a tenant that wants none of the extras pays only for building the dictionary.
        /// </summary>
        private static Dictionary<string, object> MapToListAccountFields(GetAccounts user, DateTime asOfUtc)
        {
            return new Dictionary<string, object>
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
                ["mfaEnabled"] = user.MfaEnabled,
                ["lastLoggedInTime"] = user.LastLoggedInTime,
                ["loginCount"] = user.LogInCount,
                ["createdDate"] = user.CreatedDate,
                ["roles"] = user.Roles,
                ["lockoutUntilUtc"] = user.LockoutUntilUtc,
                ["isLockedOut"] = IsLockedOut(user.LockoutUntilUtc, asOfUtc)
            };
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

        private static Dictionary<string, object> MapToSingleUserFields(GetAccounts user, string contextOrgId, DateTime asOfUtc)
        {
            if (!user.OrganizationIds.Contains(contextOrgId) && contextOrgId != "default")
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
                ["organizationIds"] = user.OrganizationIds,
                // Added AFTER the cross-org early return above, so an out-of-org caller still gets
                // an empty dictionary and no lockout state leaks across organizations.
                ["lockoutUntilUtc"] = user.LockoutUntilUtc,
                ["isLockedOut"] = IsLockedOut(user.LockoutUntilUtc, asOfUtc)
            };
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
