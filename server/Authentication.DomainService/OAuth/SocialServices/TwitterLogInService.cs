using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class TwitterLogInService : ISocialLogInService
    {
        private readonly ILogger<TwitterLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IHttpService _httpService;

        public TwitterLogInService(
            ILogger<TwitterLogInService> logger,
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
                return new SocialCallbackResult { ExternalUserData = new TwitterUserData() };
            }

            if (stateInfo.Extra == null || !stateInfo.Extra.TryGetValue("code_verifier", out var codeVerifier))
            {
                _logger.LogError("PKCE code verifier missing in stateInfo");
                return new SocialCallbackResult { ExternalUserData = new TwitterUserData() };
            }

            // Base post data (PKCE flow)
            var postData = new Dictionary<string, string>
            {
                { "grant_type", GrantTypes.AuthCode },
                { "code", stateInfo.Code },
                { "redirect_uri", stateInfo.RedirectUri },
                { "code_verifier", codeVerifier }
            };

            Dictionary<string, string>? headers = null;

            // Detect confidential client (client_secret is available)
            if (!string.IsNullOrWhiteSpace(identityProvider.ClientSecret))
            {
                var authHeader = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{identityProvider.ClientId}:{identityProvider.ClientSecret}")
                );

                headers = new Dictionary<string, string>
                {
                    { "Authorization", $"Basic {authHeader}" },
                    { "Content-Type", "application/x-www-form-urlencoded" }
                };
            }
            else
            {
                // Public client ? Twitter requires client_id in body
                postData["client_id"] = identityProvider.ClientId;
            }

            var (tokenResponse, error) = await _httpService.SendFormUrlEncoded<TwitterOauthAccessToken>(
                HttpMethod.Post,
                postData,
                identityProvider.TokenUrl,
                headers
            );

            if (!string.IsNullOrWhiteSpace(error) || tokenResponse == null)
            {
                _logger.LogError("Error getting Twitter access token: {Error}", error);
                return new SocialCallbackResult { ExternalUserData = new TwitterUserData() };
            }

            // Fetch user profile
            var profileHeaders = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {tokenResponse.AccessToken}" }
            };

            (var userProfile, var profileError) = await _httpService.Get<JsonDocument>(
                identityProvider.UserInfoUrl,
                headers: profileHeaders);

            if (!string.IsNullOrWhiteSpace(profileError))
            {
                _logger.LogError("Error fetching Twitter user profile: {ProfileError}", profileError);
                return new SocialCallbackResult { ExternalUserData = new TwitterUserData() };
            }

            if (userProfile != null)
            {
                var user = userProfile.RootElement.GetProperty("data");

                var twitterUser = new TwitterUserData
                {
                    ExternalProviderUserId = user.GetProperty("id").GetString(),
                    DisplayName = user.GetProperty("name").GetString(),
                    FirstName = user.GetProperty("name").GetString().Split(" ").First(),
                    LastName = user.GetProperty("name").GetString().Split(" ").Last(),
                    Email = user.GetProperty("confirmed_email").GetString(),
                    UserName = user.GetProperty("username").GetString(),
                    ProfileImageUrl = user.TryGetProperty("profile_image_url", out var img) ? img.GetString() : null,
                    Platform = stateInfo.Provider,
                    Roles = identityProvider?.InitialRoles ?? [],
                    Permissions = identityProvider?.InitialPermissions ?? []
                };

                return new SocialCallbackResult
                {
                    ExternalUserData = twitterUser,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken
                };
            }

            return new SocialCallbackResult { ExternalUserData = new TwitterUserData() };
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var callbackResult = await HandleSocialLoginCallback(stateInfo);
            return callbackResult.ExternalUserData;
        }
    }
}