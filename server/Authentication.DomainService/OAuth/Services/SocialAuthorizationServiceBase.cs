using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Entities;
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

        protected SocialAuthorizationServiceBase(
            ILogger logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IAuthenticationRepository oAuthRepository,
            ICacheClient cacheClient,
            ISocialLogInServiceProvider socialLogInServiceProvider)
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _oAuthRepository = oAuthRepository;
            _cacheClient = cacheClient;
            _socialLogInServiceProvider = socialLogInServiceProvider;
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, IdentityConfiguration authenticationConfiguration, User? user = null)
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
            stateInfo.FlowType = SocialFlowType.Normal;

            var socialCallbackResult = await _socialLogInServiceProvider.HandleSocialLoginCallback(stateInfo);
            var externalUser = socialCallbackResult.ExternalUserData;
            await _cacheClient.RemoveKeyAsync(request.State);

            NormalizeExternalUserEmail(externalUser);
            externalUser.Email = NormalizeEmail(externalUser.Email);

            if (string.IsNullOrWhiteSpace(externalUser.Email))
            {
                return CreateEmailNotProvidedError();
            }

            if (string.IsNullOrWhiteSpace(externalUser.ExternalProviderUserId))
            {
                return new TokenResponse { Error = "External provider did not provide any user id.", ErrorDescription = "External provider did not provide any user id", StatusCode = 401 };
            }

            var resolution = await ResolveUserAsync(stateInfo, externalUser);
            if (resolution.Error != null)
            {
                return resolution.Error;
            }

            user = resolution.User;
            var redirectUri = resolution.RedirectUrl;

            if (user == null)
            {
                if (!string.IsNullOrWhiteSpace(redirectUri))
                {
                    return new TokenResponse { SsoUserRedirectUrl = redirectUri };
                }

                return CreateUserNotFoundError(externalUser.Email ?? "");
            }

            if (!user.Active || !user.IsVerified)
            {
                return new TokenResponse { Error = "There is a user with external user id but is not active.", ErrorDescription = "There is a user with external user id but is not active", StatusCode = 401 };
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return new TokenResponse
                {
                    Error = OAuthError.AccountLocked,
                    ErrorDescription = "Account is temporarily locked due to failed authentication attempts",
                    StatusCode = 423
                };
            }

            request.OrganizationId = OrganizationAccessResolver.ResolveSignInOrganizationId(user, request.OrganizationId);
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
            return new TokenResponse { Error = "user_not_found", ErrorDescription = $"{userName} does not exist", StatusCode = 401 };
        }

        public abstract Task<(User? user, string redirectUrl)> GetUser(StateInfo stateInfo, IExternalUserData externalUser);

        /// <summary>
        /// The outcome of resolving the signing-in user. <see cref="Error"/>, when set, is returned to the caller
        /// as-is instead of the generic <c>user_not_found</c>.
        /// </summary>
        protected sealed record SocialUserResolution(User? User, string? RedirectUrl, TokenResponse? Error = null);

        /// <summary>
        /// Resolves the signing-in user. The default defers to <see cref="GetUser"/>, which can only say "found" or
        /// "not found"; a subclass overrides it when a refusal needs its own answer.
        /// </summary>
        protected virtual async Task<SocialUserResolution> ResolveUserAsync(StateInfo stateInfo, IExternalUserData externalUser)
        {
            var (user, redirectUrl) = await GetUser(stateInfo, externalUser);
            return new SocialUserResolution(user, redirectUrl);
        }

        protected static string NormalizeEmail(string? email)
        {
            return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
        }
    }
}
