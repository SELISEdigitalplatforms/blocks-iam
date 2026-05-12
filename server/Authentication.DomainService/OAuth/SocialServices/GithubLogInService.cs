using Blocks.Genesis;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Authentication.DomainService.OAuth
{
    public class GithubLogInService : ISocialLogInService
    {
        private readonly ILogger<GithubLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public GithubLogInService(
            ILogger<GithubLogInService> logger,
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
            var identityProvider = await _authenticationRepository
                .GetIdentityProviderAsync(loginData.Provider);

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
                NextUrl = loginData.NextUrl,
            };

            await _cacheClient.AddStringValueAsync(stateKey, JsonSerializer.Serialize(stateInfo), 300);

            // GitHub auth URL 
            var loginUri = $"{identityProvider.AuthorizationUrl.Split("?")[0]}?scope={identityProvider.Scope}&state={stateKey}&redirect_uri={WebUtility.UrlEncode(identityProvider.RedirectUri)}&client_id={identityProvider.ClientId}&response_type=code";

            return (loginUri, loginData.SendAsResponse);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var identityProvider = await _authenticationRepository
                .GetIdentityProviderAsync(stateInfo.Provider);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", stateInfo.Provider);
                return new GithubUserData();
            }

            var postData = new Dictionary<string, string>
            {
                { "code", stateInfo.Code },
                { "client_id", identityProvider.ClientId ?? string.Empty },
                { "client_secret", identityProvider.ClientSecret ?? string.Empty },
                { "redirect_uri", identityProvider.RedirectUri ?? string.Empty }
            };

            // Ask for JSON response
            var (tokenResponse, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(
                HttpMethod.Post,
                postData,
                identityProvider.TokenUrl,
                headers: new Dictionary<string, string> { { "Accept", "application/json" } });

            if (!string.IsNullOrWhiteSpace(error) || tokenResponse?.AccessToken == null)
            {
                _logger.LogError("Error while getting GitHub access token: {Error}", error);
                return new GithubUserData();
            }

            // Fetch GitHub user profile
            var (userResponse, userError) = await _httpService.Get<GithubUserData>(
                identityProvider.UserInfoUrl,
                headers: new Dictionary<string, string> { { "Authorization", $"Bearer {tokenResponse.AccessToken}" }, { "User-Agent", stateInfo.Audience } });

            if (!string.IsNullOrWhiteSpace(userError))
            {
                _logger.LogError("Error while getting GitHub user data: {UserError}", userError);
                return new GithubUserData();
            }

            if (string.IsNullOrEmpty(userResponse.Email))
            {
                // GitHub email endpoint (hardcoded as it's provider-specific)
                var githubEmailUrl = "https://api.github.com/user/emails";
                var (emailResponse, emailError) = await _httpService.Get<List<GithubEmail>>(
                githubEmailUrl,
                headers: new Dictionary<string, string> { { "Authorization", $"Bearer {tokenResponse.AccessToken}" },
                { "User-Agent", $"{stateInfo.Audience}" },{ "Accept", "application/vnd.github.v3+json" } });

                if (!string.IsNullOrWhiteSpace(emailError) || emailResponse == null)
                {
                    _logger.LogError("Error while getting GitHub user email: {EmailError}", emailError);
                    return userResponse;
                }
                userResponse.Email = emailResponse.FirstOrDefault(e => e.Primary && e.Verified)?.Email
                       ?? emailResponse.FirstOrDefault(e => e.Verified)?.Email
                       ?? emailResponse.FirstOrDefault()?.Email;
            }

            userResponse.ExternalProviderUserId = userResponse.Id.ToString();
            userResponse.Permissions = identityProvider?.InitialPermissions ?? [];
            userResponse.Roles = identityProvider?.InitialRoles ?? [];
            userResponse.Platform = stateInfo.Provider;

            return userResponse;
        }
    }
}
