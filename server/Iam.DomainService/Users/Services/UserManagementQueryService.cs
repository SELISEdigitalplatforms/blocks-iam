using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Users
{
    public class UserManagementQueryService : IUserManagementQueryService
    {
        private readonly ILogger<UserManagementQueryService> _logger;
        private readonly IUserRepository _userRepository;
        private readonly TimeProvider _timeProvider;

        public UserManagementQueryService(
            ILogger<UserManagementQueryService> logger,
            IUserRepository userRepository,
            // Optional with a System fallback rather than a DI registration: this service is
            // registered from Authentication.DomainService's RegisterAllServices (the root the API
            // actually calls), so a required dependency registered elsewhere would be unresolvable
            // at runtime while every unit test still passed.
            TimeProvider? timeProvider = null
        )
        {
            _logger = logger;
            _userRepository = userRepository;
            _timeProvider = timeProvider ?? TimeProvider.System;
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

            var (data, count) = await _userRepository.GetUsersAsync<GetAccounts, GetUsersRequest>(query);

            // One instant for the whole response, per the data contract's "server UTC time at
            // response construction" - so every item in a page is judged against the same moment.
            // Lazy so an empty page never reads the clock at all: C2 says no lockout computation is
            // attempted when nothing matches, and a test with a throwing clock proves it.
            var asOfUtc = new Lazy<DateTime>(() => _timeProvider.GetUtcNow().UtcDateTime);

            var selectedUsers = data?.Select(user => MapToListAccountFields(user, asOfUtc.Value))
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
            var contextOrgId = string.IsNullOrWhiteSpace(organizationId) ? (bc?.OrganizationId ?? "default") : organizationId;

            // Read only when there is a user to map - a missing user must not trigger any lockout
            // computation (C4), and a throwing clock in the tests proves it does not.
            var data = user == null
                ? null
                : MapToSingleUserFields(user, contextOrgId, _timeProvider.GetUtcNow().UtcDateTime);

            if(contextOrgId == "default")
            {
                data.Add("OrganizationsRoles", user.Roles);
                data.Add("OrganizationsPermissions", user.Permissions);
            }

            _logger.LogInformation("User get end");

            return new GetUserResponse
            {
                Data = data
            };
        }

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
