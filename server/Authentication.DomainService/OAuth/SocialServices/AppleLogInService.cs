using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;


namespace Authentication.DomainService.OAuth.SocialServices
{
    public class AppleLogInService : ISocialLogInService
    {
        private readonly ILogger<AppleLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IHttpService _httpService;

        public AppleLogInService(
            ILogger<AppleLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService
        )
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
            await _cacheClient.AddStringValueAsync(
                stateKey,
                System.Text.Json.JsonSerializer.Serialize(stateInfo),
                300
            );

            var authorizationUrl = string.Format(
                identityProvider.AuthorizationUrl,
                identityProvider.ClientId,                              
                WebUtility.UrlEncode(identityProvider.Scope),           
                WebUtility.UrlEncode(identityProvider.RedirectUri),     
                stateKey                                          
            );

            return (authorizationUrl, loginData.SendAsResponse);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var identityProvider = await _authenticationRepository.GetIdentityProviderAsync(stateInfo.Provider);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", stateInfo.Provider);
                return new AppleUserData();
            }

            var postData = new Dictionary<string, string>
                {
                    { "code", stateInfo.Code },
                    { "client_id", identityProvider.ClientId },
                    { "client_secret",  GenerateClientSecret(identityProvider)},
                    { "redirect_uri", identityProvider.RedirectUri },
                    { "grant_type", "authorization_code" }
                };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, identityProvider.TokenUrl);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogError("Error while getting access token: {Error}", error);
                return new AppleUserData();
            }

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(response.IdToken);
            var payload = JsonConvert.SerializeObject(jwtToken.Payload);
            var deserializeAppleIdToken = JsonConvert.DeserializeObject<AppleIdToken>(payload);
            var appleUserData = new AppleUserData();
            appleUserData.Email = deserializeAppleIdToken.Email;
            appleUserData.ExternalProviderUserId = deserializeAppleIdToken.ExternalProviderUserId;
            appleUserData.Roles = identityProvider?.InitialRoles ?? [];
            appleUserData.Platform = stateInfo.Provider;
            return appleUserData;
        }
        public string GenerateClientSecret(IdentityProvider identityProvider)
        {
            string teamId = identityProvider.TeamId ?? "";
            string clientId = identityProvider.ClientId;
            string keyId = identityProvider.KeyId ?? "";
            var privateKey = identityProvider.PrivateKey ?? "";
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKey);
            var securityKey = new ECDsaSecurityKey(ecdsa)
            {
                KeyId = keyId
            };
            var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var payload = new JwtPayload
            {
                { "iss", teamId },
                { "iat", now },
                { "exp", now + 300 },
                { "aud", identityProvider.AppleAudience ?? "https://appleid.apple.com" },
                { "sub", clientId }
            };
            var header = new JwtHeader(signingCredentials);
            var jwt = new JwtSecurityToken(header, payload);
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

    }
}
