using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;


namespace Authentication.DomainService.OAuth.SocialServices
{
    public class FaceBookLogInService : ISocialLogInService
    {
        private readonly ILogger<FaceBookLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IHttpService _httpService;

        public FaceBookLogInService(
            ILogger<FaceBookLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            IHttpService httpService)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _httpService = httpService;
        }

        public async Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo)
        {
            var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(stateInfo.ClientId);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", stateInfo.Provider);
                return new SocialCallbackResult { ExternalUserData = new FaceBookUserData() };
            }

            string faceBookGetAccessTokenUri = string.Format("{0}?client_id={1}&redirect_uri={2}&client_secret={3}&code={4}", identityProvider.TokenUrl, identityProvider.ClientId, stateInfo.RedirectUri, identityProvider.ClientSecret, stateInfo.Code);
            _logger.LogInformation("faceBook Access Token Uri {AccessTokenUri}", faceBookGetAccessTokenUri);
            var (tokenResponse, error) = await _httpService.Get<SocialOauthAccessToken>(faceBookGetAccessTokenUri);

            if (!string.IsNullOrWhiteSpace(error) || tokenResponse == null)
            {
                _logger.LogError("Error getting facebook access token: {Error}", error);
                return new SocialCallbackResult { ExternalUserData = new FaceBookUserData() };
            }
            var profileHeaders = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {tokenResponse.AccessToken}" }
            };

            (var faceBookUserData, var profileError) = await _httpService.Get<FaceBookUserData>(
                identityProvider.UserInfoUrl,
                headers: profileHeaders);

            if (!string.IsNullOrWhiteSpace(profileError))
            {
                _logger.LogError("Error fetching Facebook user profile: {ProfileError}", profileError);
                return new SocialCallbackResult { ExternalUserData = new FaceBookUserData() };
            }

            return new SocialCallbackResult
            {
                ExternalUserData = faceBookUserData,
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
