using Authentication.DomainService.Services;
using Authentication.DomainService.OAuth.SocialServices;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Authentication.DomainService.OAuth
{
    public class BYOSsoLogInService : SocialLogInServiceBase
    {
        private readonly IExternalUserMapperRegistry _mapperRegistry;

        public BYOSsoLogInService(
            ILogger<BYOSsoLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            IHttpService httpService,
            IExternalUserMapperRegistry mapperRegistry
        ) : base(logger, authenticationRepository, httpService)
        {
            _mapperRegistry = mapperRegistry;
        }

        public override async Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo)
        {
            var identityProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(stateInfo.ClientId);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", stateInfo.ClientId);
                return new SocialCallbackResult { ExternalUserData = new BYOSsoUserData() };
            }

            var postData = new Dictionary<string, string>
                {
                    { "code", stateInfo.Code },
                    { "client_id", identityProvider.ClientId },
                    { "client_secret", identityProvider.ClientSecret },
                    { "redirect_uri", stateInfo.RedirectUri },
                    { "grant_type", GrantTypes.AuthCode }
                };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, identityProvider.TokenUrl);
            if (!string.IsNullOrWhiteSpace(error) || response == null)
            {
                _logger.LogError("Error while getting access token: {Error}", error);
                return new SocialCallbackResult { ExternalUserData = new BYOSsoUserData() };
            }

            var result = await _httpService.Get<dynamic>(identityProvider.UserInfoUrl, new Dictionary<string, string> {
                { "Authorization", $"bearer {response.AccessToken}"  } });

            if (!string.IsNullOrWhiteSpace(result.Item2) || result.Item1 == null)
            {
                _logger.LogError("Error while getting user data: {Error}", result.Item2);
                return new SocialCallbackResult { ExternalUserData = new BYOSsoUserData() };
            }

            var payload = (JsonElement)result.Item1;
            var externalUser = new BYOSsoUserData();
            _mapperRegistry.Map(stateInfo.Provider, payload, externalUser);
            externalUser.Permissions = identityProvider?.InitialPermissions ?? [];
            externalUser.Roles = identityProvider?.InitialRoles ?? [];
            externalUser.Platform = stateInfo.Provider;

            return new SocialCallbackResult
            {
                ExternalUserData = externalUser,
                AccessToken = response.AccessToken,
                IdToken = response.IdToken,
                RefreshToken = response.RefreshToken
            };
        }

        public override async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var callbackResult = await HandleSocialLoginCallback(stateInfo);
            return callbackResult.ExternalUserData;
        }

        protected override IExternalUserData CreateEmptyUserData()
        {
            return new BYOSsoUserData();
        }
    }

}
