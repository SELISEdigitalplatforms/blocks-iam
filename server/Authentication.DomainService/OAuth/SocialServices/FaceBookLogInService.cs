using Blocks.Genesis;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Net;


namespace Authentication.DomainService.OAuth.SocialServices
{
    public class FaceBookLogInService : ISocialLogInService
    {
        private readonly ILogger<FaceBookLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public FaceBookLogInService(
            ILogger<FaceBookLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _httpService = httpService;
        }
        public async Task<(string, bool)> GetProviderLogInUriAsync(GetSocialLogInEndPointRequest loginData)
        {
            var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(loginData.Provider);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", loginData.Provider);
                return (string.Empty, true);
            }

            var stateKey = Guid.NewGuid().ToString("n");
            var stateInfo = new StateInfo
            {
                Audience = loginData.Audience,
                Provider = loginData.Provider,
                NextUrl = loginData.NextUrl ?? string.Empty
            };

            await _cacheClient.AddStringValueAsync(stateKey, System.Text.Json.JsonSerializer.Serialize(stateInfo), 300);

            var loginUri = string.Format(
                identityProvider.AuthorizationUrl,
                identityProvider.ClientId,
                WebUtility.UrlEncode(identityProvider.RedirectUri),
                WebUtility.UrlEncode(identityProvider.Scope),
                stateKey
            );

            return (loginUri, loginData.SendAsResponse);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(stateInfo.Provider);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", stateInfo.Provider);
                return new FaceBookUserData();
            }

            string faceBookGetAccessTokenUri = string.Format("{0}?client_id={1}&redirect_uri={2}&client_secret={3}&code={4}",identityProvider.TokenUrl, identityProvider.ClientId, identityProvider.RedirectUri, identityProvider.ClientSecret, stateInfo.Code);
            _logger.LogInformation("faceBook Access Token Uri {AccessTokenUri}", faceBookGetAccessTokenUri);
            var (tokenResponse, error) = await _httpService.Get<SocialOauthAccessToken>(faceBookGetAccessTokenUri);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error getting facebook access token: {Error}", error);
                return new FaceBookUserData();
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
                return new FaceBookUserData();
            }
            return faceBookUserData;

        }
    }
}
