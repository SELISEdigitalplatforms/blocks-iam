using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class LinkedinLogInService : ISocialLogInService
    {
        private readonly ILogger<LinkedinLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public LinkedinLogInService(
            ILogger<LinkedinLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            IHttpService httpService)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _httpService = httpService;
        }

        public async Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo)
        {
            var identityProvider = await _authenticationRepository
                .GetIdentityProviderByClientIdAsync(stateInfo.ClientId);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", stateInfo.Provider);
                return new SocialCallbackResult { ExternalUserData = new LinkedinUserData() };
            }

            var postData = new Dictionary<string, string>
            {
                { "code", stateInfo.Code },
                { "client_id", identityProvider.ClientId },
                { "client_secret", identityProvider.ClientSecret },
                { "redirect_uri", stateInfo.RedirectUri },
                { "grant_type", "authorization_code" }
            };

            var (tokenResponse, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(
                HttpMethod.Post,
                postData,
                identityProvider.TokenUrl);

            if (!string.IsNullOrWhiteSpace(error) || tokenResponse == null)
            {
                _logger.LogError("Error while getting LinkedIn access token: {Error}", error);
                return new SocialCallbackResult { ExternalUserData = new LinkedinUserData() };
            }

            var profileUrl = identityProvider.UserInfoUrl + $"?oauth2_access_token={tokenResponse.AccessToken}";

            (var userInfo, var profileError) = await _httpService.Get<LinkedinUserInfo>(
                profileUrl);

            var profile = new LinkedinUserData
            {
                ExternalProviderUserId = userInfo.Sub,
                FirstName = userInfo.Given_Name,
                LastName = userInfo.Family_Name,
                Email = userInfo.Email,
                DisplayName = userInfo.Name,
                ProfileImageUrl = userInfo.Picture
            };

            if (!string.IsNullOrWhiteSpace(profileError))
            {
                _logger.LogError("Error while getting LinkedIn user profile: {ProfileError}", profileError);
                return new SocialCallbackResult { ExternalUserData = new LinkedinUserData() };
            }

            profile.Permissions = identityProvider?.InitialPermissions ?? [];
            profile.Roles = identityProvider?.InitialRoles ?? [];
            profile.Platform = stateInfo.Provider;

            return new SocialCallbackResult
            {
                ExternalUserData = profile,
                AccessToken = tokenResponse.AccessToken,
                IdToken = tokenResponse.IdToken,
                RefreshToken = tokenResponse.RefreshToken
            };
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var callbackResult = await HandleSocialLoginCallback(stateInfo);
            return callbackResult.ExternalUserData;
        }
    }
}
