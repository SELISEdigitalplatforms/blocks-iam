using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Iam.DomainService.Utilities;
using Authentication.DomainService.Shared;
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
        private const string GoogleProvider = "google";
        private const string MicrosoftProvider = "microsoft";

        // department and employeeId are directory-only attributes. Personal Microsoft accounts
        // cannot expose them, and Graph rejects the whole projection with 403 rather than
        // returning them as null, so the consumer-safe set omits both.
        private const string MicrosoftDirectoryProfileSelect =
            "displayName,mail,department,employeeId,givenName,userPrincipalName,surname,officeLocation,preferredLanguage,mobilePhone,id";
        private const string MicrosoftConsumerProfileSelect =
            "displayName,mail,givenName,userPrincipalName,surname,officeLocation,preferredLanguage,mobilePhone,id";

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
                { "grant_type", GrantTypes.AuthCode },
                { "scope", IdpConstants.OpenIdProfileEmailScope }
            };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, identityProvider.TokenUrl);

            if (!string.IsNullOrWhiteSpace(error) || response == null)
            {
                _logger.LogError("Error while getting access token: {Error}", error);
                return new SocialCallbackResult { ExternalUserData = CreateEmptyUserData() };
            }

            var externalUser = stateInfo.Provider switch
            {
                GoogleProvider => await GetGoogleProfileVerification(identityProvider.UserInfoUrl, response.AccessToken),
                MicrosoftProvider => await GetMicrosoftProfileVerification(identityProvider, response.AccessToken, response.IdToken),
                _ => CreateEmptyUserData()
            };

            externalUser.Permissions = identityProvider.InitialPermissions;

            _logger.LogDebug("IntraId Roles: {Roles}", string.Join(", ", externalUser.Roles));

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

            if (!string.IsNullOrWhiteSpace(error) || externalUser == null)
            {
                _logger.LogError("Error while getting google user data: {Error}", error);
                return CreateEmptyUserData();
            }

            return externalUser;
        }
        private async Task<IExternalUserData> GetMicrosoftProfileVerification(IdentityProvider identityProvider, string accessToken, string idToken)
        {
            var headers = new Dictionary<string, string> { { "Authorization", $"bearer {accessToken}" } };

            var externalUser = await GetMicrosoftGraphProfile(identityProvider.UserInfoUrl, headers) ?? CreateEmptyUserData();

            // Graph is best-effort for Microsoft: personal accounts can fail it outright. The
            // id_token from the token exchange carries the same identity, so fill the gaps from
            // there rather than rejecting the login for a missing email.
            ApplyMicrosoftIdTokenFallbacks(externalUser, idToken, identityProvider);

            externalUser.Roles = ExtractRolesFromJwtOrEmpty(idToken);

            return externalUser;
        }

        private async Task<IExternalUserData?> GetMicrosoftGraphProfile(string profileURL, Dictionary<string, string> headers)
        {
            var (externalUser, error) = await _httpService.Get<MicrosoftUserData>(
                $"{profileURL}?$select={MicrosoftDirectoryProfileSelect}", headers);

            if (string.IsNullOrWhiteSpace(error) && externalUser != null)
            {
                return externalUser;
            }

            // Retry without the directory-only attributes before giving up on Graph. Organizational
            // accounts never reach this, so they keep their single round trip and their
            // department/employeeId values.
            _logger.LogWarning("Microsoft profile fetch failed, retrying without directory-only attributes: {Error}", error);

            (externalUser, error) = await _httpService.Get<MicrosoftUserData>(
                $"{profileURL}?$select={MicrosoftConsumerProfileSelect}", headers);

            if (string.IsNullOrWhiteSpace(error) && externalUser != null)
            {
                return externalUser;
            }

            _logger.LogError("Error while getting microsoft user data: {Error}", error);
            return null;
        }

        private void ApplyMicrosoftIdTokenFallbacks(IExternalUserData externalUser, string idToken, IdentityProvider identityProvider)
        {
            var token = ReadTrustedIdToken(idToken, identityProvider);

            if (token == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(externalUser.Email) && string.IsNullOrWhiteSpace(externalUser.UserPrincipalName))
            {
                SetIfEmpty(FirstClaim(token, "preferred_username", "email", "upn"), v => externalUser.Email = v);
            }

            if (string.IsNullOrWhiteSpace(externalUser.ExternalProviderUserId))
            {
                SetIfEmpty(FirstClaim(token, "oid", "sub"), v => externalUser.ExternalProviderUserId = v);
            }

            if (string.IsNullOrWhiteSpace(externalUser.DisplayName))
            {
                SetIfEmpty(FirstClaim(token, "name"), v => externalUser.DisplayName = v);
            }

            if (string.IsNullOrWhiteSpace(externalUser.FirstName))
            {
                SetIfEmpty(FirstClaim(token, "given_name"), v => externalUser.FirstName = v);
            }

            if (string.IsNullOrWhiteSpace(externalUser.LastName))
            {
                SetIfEmpty(FirstClaim(token, "family_name"), v => externalUser.LastName = v);
            }
        }

        /// <summary>
        /// Reads the id_token without validating its signature — it arrives back-channel over TLS
        /// straight from the configured token endpoint (OIDC Core 3.1.3.7). aud and iss are still
        /// checked, because these claims decide which user is signed in rather than merely what
        /// roles they receive.
        /// </summary>
        private JwtSecurityToken? ReadTrustedIdToken(string idToken, IdentityProvider identityProvider)
        {
            if (string.IsNullOrWhiteSpace(idToken))
            {
                return null;
            }

            JwtSecurityToken token;

            try
            {
                token = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microsoft id_token could not be parsed, skipping claim fallback");
                return null;
            }

            if (!token.Audiences.Contains(identityProvider.ClientId, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Microsoft id_token audience does not match the configured client, skipping claim fallback");
                return null;
            }

            if (!IssuerMatchesTokenEndpoint(token.Issuer, identityProvider.TokenUrl))
            {
                _logger.LogWarning("Microsoft id_token issuer {Issuer} does not match the configured token endpoint, skipping claim fallback", token.Issuer);
                return null;
            }

            return token;
        }

        private static bool IssuerMatchesTokenEndpoint(string issuer, string tokenUrl)
        {
            return Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri)
                && Uri.TryCreate(tokenUrl, UriKind.Absolute, out var tokenUri)
                && string.Equals(issuerUri.Host, tokenUri.Host, StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstClaim(JwtSecurityToken token, params string[] claimTypes)
        {
            foreach (var claimType in claimTypes)
            {
                var value = token.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static void SetIfEmpty(string value, Action<string> assign)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                assign(value);
            }
        }

        private List<string> ExtractRolesFromJwtOrEmpty(string jwt)
        {
            if (string.IsNullOrWhiteSpace(jwt))
            {
                return [];
            }

            try
            {
                return ExtractRolesFromJwt(jwt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microsoft id_token roles could not be read");
                return [];
            }
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