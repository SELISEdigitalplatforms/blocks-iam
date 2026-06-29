using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    public sealed class GithubLogInService : ISocialLogInService
    {
        private readonly ILogger<GithubLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IHttpService _httpService;

        public GithubLogInService(
            ILogger<GithubLogInService> logger,
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
                return new SocialCallbackResult { ExternalUserData = new GithubUserData() };
            }

            var postData = new Dictionary<string, string>
            {
                { "code", stateInfo.Code },
                { "client_id", identityProvider.ClientId ?? string.Empty },
                { "client_secret", identityProvider.ClientSecret ?? string.Empty },
                { "redirect_uri", stateInfo.RedirectUri ?? string.Empty }
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
                return new SocialCallbackResult { ExternalUserData = new GithubUserData() };
            }

            // Fetch GitHub user profile
            var (userResponse, userError) = await _httpService.Get<GithubUserData>(
                identityProvider.UserInfoUrl,
                headers: new Dictionary<string, string> { { "Authorization", $"Bearer {tokenResponse.AccessToken}" }, { "User-Agent", stateInfo.Audience } });

            if (!string.IsNullOrWhiteSpace(userError))
            {
                _logger.LogError("Error while getting GitHub user data: {UserError}", userError);
                return new SocialCallbackResult { ExternalUserData = new GithubUserData() };
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
                    return new SocialCallbackResult
                    {
                        ExternalUserData = userResponse,
                        AccessToken = tokenResponse.AccessToken,
                        IdToken = tokenResponse.IdToken,
                        RefreshToken = tokenResponse.RefreshToken
                    };
                }
                userResponse.Email = emailResponse.FirstOrDefault(e => e.Primary && e.Verified)?.Email
                       ?? emailResponse.FirstOrDefault(e => e.Verified)?.Email
                       ?? emailResponse.FirstOrDefault()?.Email;
            }

            userResponse.ExternalProviderUserId = userResponse.Id.ToString();
            userResponse.Permissions = identityProvider?.InitialPermissions ?? [];
            userResponse.Roles = identityProvider?.InitialRoles ?? [];
            userResponse.Platform = stateInfo.Provider;

            return new SocialCallbackResult
            {
                ExternalUserData = userResponse,
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
