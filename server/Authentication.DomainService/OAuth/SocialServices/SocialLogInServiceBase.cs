using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace Authentication.DomainService.OAuth
{
    public abstract class SocialLogInServiceBase : ISocialLogInService
    {
        protected readonly ILogger _logger;
        protected readonly IAuthenticationRepository _authenticationRepository;
        protected readonly ICacheClient _cacheClient;
        protected readonly IHttpService _httpService;
        private const string googleProvider = "google";
        private const string microsoftProvider = "microsoft";

        protected SocialLogInServiceBase(
            ILogger logger,
            IAuthenticationRepository authenticationRepository,
            IHttpService httpService
        )
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
            _httpService = httpService;
        }

        public virtual async Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo)
        {
            var identityProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(stateInfo.ClientId);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", stateInfo.Provider);
                return new SocialCallbackResult { ExternalUserData = CreateEmptyUserData() };
            }

            var postData = new Dictionary<string, string>
            {
                { "code", stateInfo.Code ?? string.Empty },
                { "client_id", identityProvider.ClientId ?? string.Empty },
                { "client_secret", identityProvider.ClientSecret ?? string.Empty },
                { "redirect_uri", stateInfo.RedirectUri ?? string.Empty },
                { "grant_type", "authorization_code" },
                { "scope", "openid profile email" }
            };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, identityProvider.TokenUrl);

            if (!string.IsNullOrWhiteSpace(error) || response == null)
            {
                _logger.LogError("Error while getting access token: {Error}", error);
                return new SocialCallbackResult { ExternalUserData = CreateEmptyUserData() };
            }

            var externalUser = stateInfo.Provider switch
            {
                googleProvider => await GetGoogleProfileVerification(identityProvider.UserInfoUrl, response.AccessToken),
                microsoftProvider => await GetMicrosoftProfileVerification(identityProvider.UserInfoUrl, response.AccessToken, response.IdToken),
                _ => CreateEmptyUserData()
            };

            externalUser.Permissions = identityProvider.InitialPermissions;

            Console.WriteLine($"IntraId Roles: {string.Join(", ", externalUser.Roles)}");

            if (externalUser.Roles.Count > 0)
                externalUser.Roles.AddRange(identityProvider.InitialRoles);
            else
                externalUser.Roles = identityProvider.InitialRoles;

            externalUser.Platform = stateInfo.Provider;

            return new SocialCallbackResult
            {
                ExternalUserData = externalUser,
                AccessToken = response.AccessToken,
                IdToken = response.IdToken,
                RefreshToken = response.RefreshToken
            };
        }

        public virtual async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var callbackResult = await HandleSocialLoginCallback(stateInfo);
            return callbackResult.ExternalUserData;
        }

        protected abstract IExternalUserData CreateEmptyUserData();
        private async Task<IExternalUserData> GetGoogleProfileVerification(string profileURL, string accessToken)
        {
            var userAccessEndPoint = string.Format(profileURL, accessToken);

            var (externalUser, error) = await _httpService.Get<GoogleUserData>(userAccessEndPoint);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error while getting google user data: {Error}", error);
                return new GoogleUserData();
            }
            if (!string.IsNullOrWhiteSpace(error) || externalUser == null)
            {
                _logger.LogError("Error while getting user data: {Error}", error);
                return CreateEmptyUserData();
            }

            return externalUser;
        }
        private async Task<IExternalUserData> GetMicrosoftProfileVerification(string profileURL, string accessToken, string idToken)
        {
            var queryPram = "$select=displayName,mail,department,employeeId,givenName,userPrincipalName,surname,officeLocation,preferredLanguage,mobilePhone,id";
            profileURL = $"{profileURL}?{queryPram}";

            var (externalUser, error) = await _httpService.Get<MicrosoftUserData>(profileURL, new Dictionary<string, string> {
                { "Authorization", $"bearer {accessToken}"  }
            });

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error while getting microsoft user data: {Error}", error);
                return new MicrosoftUserData();
            }
            if (!string.IsNullOrWhiteSpace(error) || externalUser == null)
            {
                _logger.LogError("Error while getting user data: {Error}", error);
                return CreateEmptyUserData();
            }

            externalUser.Roles = ExtractRolesFromJwt(idToken);

            return externalUser;
        }


        public static List<string> ExtractRolesFromJwt(string jwt)
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwt); // NOTE: this does NOT validate signature

            // Look for "roles" claim (your token uses "roles")
            var rolesClaim = token.Claims.FirstOrDefault(c => c.Type == "roles")?.Value;

            if (string.IsNullOrWhiteSpace(rolesClaim))
                return new List<string>();

            // If roles is a JSON array, parse it; otherwise treat as single role string
            rolesClaim = rolesClaim.Trim();

            if (rolesClaim.StartsWith("["))
            {
                return JsonSerializer.Deserialize<List<string>>(rolesClaim) ?? new List<string>();
            }

            return new List<string> { rolesClaim };
        }
    }
}