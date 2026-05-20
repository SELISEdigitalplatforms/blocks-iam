using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Authentication.DomainService.OAuth.Services
{
    public abstract class SocialAuthorizationServiceBase : ITokenService
    {
        protected readonly ILogger _logger;
        protected readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        protected readonly IAuthenticationRepository _oAuthRepository;
        protected readonly ICacheClient _cacheClient;
        protected readonly ISocialLogInServiceProvider _socialLogInServiceProvider;
        protected readonly IUserManagementMutationService _userManagementMutationService;
        private readonly IConfiguration _configuration;

        protected SocialAuthorizationServiceBase(
            ILogger logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationRepository oAuthRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider,
            IUserManagementMutationService userManagementMutationService,
            IConfiguration configuration)
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _oAuthRepository = oAuthRepository;
            _cacheClient = cacheClient;
            _socialLogInServiceProvider = socialLogInServiceProvider;
            _userManagementMutationService = userManagementMutationService;
            _configuration = configuration;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            _logger.LogInformation("Social Authentication start");

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                _logger.LogError("Code is required");
                return new TokenResponse { Error = "code_require", ErrorDescription = "code_require", StatusCode = 400 };
            }

            if (string.IsNullOrWhiteSpace(request.State))
            {
                return new TokenResponse { Error = "state_require", ErrorDescription = "state_require", StatusCode = 400 };
            }

            var stateCacheData = await _cacheClient.GetStringValueAsync(request.State);

            if (string.IsNullOrWhiteSpace(stateCacheData))
            {
                _logger.LogError("State data not found");
                return new TokenResponse { Error = "state_data_not_found", ErrorDescription = "state_data_not_found", StatusCode = 400 };
            }

            var stateInfo = JsonSerializer.Deserialize<StateInfo>(stateCacheData);
            if (stateInfo == null)
            {
                _logger.LogError("State data is invalid");
                return new TokenResponse { Error = "state_data_invalid", ErrorDescription = "state_data_invalid", StatusCode = 400 };
            }

            stateInfo.Code = request.Code;

            var externalUser = await _socialLogInServiceProvider.HandleSocialLogin(stateInfo);
            await _cacheClient.RemoveKeyAsync(request.State);

            NormalizeExternalUserEmail(externalUser);

            if (string.IsNullOrWhiteSpace(externalUser.Email))
            {
                return CreateEmailNotProvidedError();
            }

            if (string.IsNullOrWhiteSpace(externalUser.ExternalProviderUserId))
            {
                return new TokenResponse { Error = "External provider did not provide any user id.", ErrorDescription = "External provider did not provide any user id", StatusCode = 401 };
            }

            (user, string redirectUri) = await GetUser(stateInfo, externalUser);

            if (user == null)
            {
                if(!string.IsNullOrWhiteSpace(redirectUri))
                {
                    return new TokenResponse { SsoUserRedirectUrl = redirectUri };
                }
               
                return CreateUserNotFoundError(externalUser.Email?? "");
            }

            if (!user.Active || !user.IsVerified)
            {
                return new TokenResponse { Error = "There is a user with external user id but is not active.", ErrorDescription = "There is a user with external user id but is not active", StatusCode = 401 };
            }

            request.OrganizationId = ResolveSignInOrganizationId(user, request.OrganizationId);
            var tokenResponse = await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);

            if (string.IsNullOrWhiteSpace(tokenResponse.Error)
                && !string.IsNullOrWhiteSpace(request.OrganizationId)
                && !string.Equals(user.LastUsedOrganizationId, request.OrganizationId, StringComparison.OrdinalIgnoreCase))
            {
                await _oAuthRepository.UpdatePartialAsync<User>(
                    user.ItemId,
                    new Dictionary<string, object>
                    {
                        { nameof(User.LastUsedOrganizationId), request.OrganizationId },
                        { nameof(User.LastUpdatedDate), DateTime.UtcNow },
                        { nameof(User.LastUpdatedBy), user.ItemId }
                    });
            }

            return tokenResponse;
        }

        protected virtual void NormalizeExternalUserEmail(IExternalUserData externalUser)
        {
            // Default implementation does nothing
            // Override in derived classes if needed
        }

        protected virtual TokenResponse CreateEmailNotProvidedError()
        {
            return new TokenResponse { Error = "External provider did not provide any email.", ErrorDescription = "External provider did not provide any email", StatusCode = 401 };
        }

        protected virtual TokenResponse CreateUserNotFoundError(string userName)
        {
            return new TokenResponse { Error = "user_not_found", ErrorDescription = $"{userName} is not exit", StatusCode = 401 };
        }

        public abstract Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser);

        private static string ResolveSignInOrganizationId(User user, string? requestedOrganizationId)
        {
            if (HasOrganizationAccess(user, requestedOrganizationId))
            {
                return requestedOrganizationId!;
            }

            if (HasOrganizationAccess(user, user.LastUsedOrganizationId))
            {
                return user.LastUsedOrganizationId!;
            }

            if (HasOrganizationAccess(user, "default"))
            {
                return "default";
            }

            return user.OrganizationIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                ?? user.Roles.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? user.Permissions.Keys.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
                ?? "default";
        }

        private static bool HasOrganizationAccess(User user, string? organizationId)
        {
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                return false;
            }

            return user.OrganizationIds.Contains(organizationId)
                || user.Roles.ContainsKey(organizationId)
                || user.Permissions.ContainsKey(organizationId);
        }
    }
}
