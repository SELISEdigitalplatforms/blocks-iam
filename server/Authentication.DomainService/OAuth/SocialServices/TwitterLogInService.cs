using Blocks.Genesis;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public class TwitterLogInService : ISocialLogInService
    {
        private readonly ILogger<TwitterLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public TwitterLogInService(
            ILogger<TwitterLogInService> logger,
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

            // Generate random state
            var stateKey = Guid.NewGuid().ToString("n");

            // PKCE code verifier & challenge
            var codeVerifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            using var sha256 = SHA256.Create();
            var codeChallenge = Convert.ToBase64String(
                    sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier)))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            var stateInfo = new StateInfo
            {
                Audience = loginData.Audience,
                Provider = loginData.Provider,
                NextUrl = loginData.NextUrl,
                Extra = new Dictionary<string, string> { { "code_verifier", codeVerifier } }
            };

            await _cacheClient.AddStringValueAsync(stateKey, JsonSerializer.Serialize(stateInfo), 300);

            var loginUri =
                $"{identityProvider.AuthorizationUrl}?response_type=code" +
                $"&client_id={identityProvider.ClientId}" +
                $"&redirect_uri={WebUtility.UrlEncode(identityProvider.RedirectUri)}" +
                $"&scope={WebUtility.UrlEncode(identityProvider.Scope).Replace("+", "%20")}" +
                $"&state={stateKey}" +
                $"&code_challenge={codeChallenge}" +
                $"&code_challenge_method=S256";

            return (loginUri, loginData.SendAsResponse);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var identityProvider = await _authenticationRepository
                .GetIdentityProviderAsync(stateInfo.Provider);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", stateInfo.Provider);
                return new TwitterUserData();
            }

            if (!stateInfo.Extra.TryGetValue("code_verifier", out var codeVerifier))
            {
                _logger.LogError("PKCE code verifier missing in stateInfo");
                return new TwitterUserData();
            }

            // Base post data (PKCE flow)
            var postData = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", stateInfo.Code },
                { "redirect_uri", identityProvider.RedirectUri },
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

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error getting Twitter access token: {Error}", error);
                return new TwitterUserData();
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
                return new TwitterUserData();
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

                return twitterUser;
            }

            return new TwitterUserData();
        }
    }
}