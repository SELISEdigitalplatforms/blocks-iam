using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Users
{
    public class UserManagementQueryService : IUserManagementQueryService
    {
        private readonly ILogger<UserManagementQueryService> _logger;
        private readonly IUserRepository _userRepository;

        public UserManagementQueryService(
            ILogger<UserManagementQueryService> logger,
            IUserRepository userRepository
        )
        {
            _logger = logger;
            _userRepository = userRepository;
        }

        public async Task<bool> IsUserAvailableAsync(IsEmailAvailableRequest query)
        {
            _logger.LogInformation("User existance search start");

            var user = await _userRepository.GetUserByEmailAsync(query.Email.ToLower());

            _logger.LogInformation("User existance search end");

            return user == null;
        }

        public async Task<GetUsersResponse> GetUsersAsync(GetUsersRequest query)
        {
            _logger.LogInformation("User get start");

            query.Filter ??= new GetUsersFilter();

            var (data, count) = await _userRepository.GetUsersAsync<GetAccounts, GetUsersRequest>(query);

            var contextOrgId = string.IsNullOrWhiteSpace(query.Filter.OrganizationId) ? "default" : query.Filter.OrganizationId;
            var selectedUsers = data?.Select(user => MapToListAccountFields(user, contextOrgId)).AsQueryable() ?? Enumerable.Empty<Dictionary<string, object>>().AsQueryable();

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

            var data = user == null ? null : MapToSingleUserFields(user, contextOrgId);

            _logger.LogInformation("User get end");

            return new GetUserResponse
            {
                Data = data
            };
        }

        public async Task<List<UserTimeline>> GetUserTimelinesAsync(GetUserTimeLineRequest request)
        {
            return await _userRepository.GetUserTimelinesAsync(request);
        }

        private static Dictionary<string, object> MapToListAccountFields(GetAccounts user, string contextOrgId)
        {
            if(!user.OrganizationIds.Contains(contextOrgId))
            {
                return new Dictionary<string, object>();
            }

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
                ["createdDate"] = user.CreatedDate
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

        private static Dictionary<string, object> MapToSingleUserFields(GetAccounts user, string contextOrgId)
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
                ["OrganizationIds"] = user.OrganizationIds
            };
        }
    }
}
