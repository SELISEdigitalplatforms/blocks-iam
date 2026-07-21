using Authentication.DomainService.Entities;
using Iam.DomainService.Utilities;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;


namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class AppleLogInService : ISocialLogInService
    {
        private readonly ILogger<AppleLogInService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;
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
            _httpService = httpService;
        }

        public async Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo)
        {
            var identityProvider = await _authenticationRepository.GetIdentityProviderByClientIdAsync(stateInfo.ClientId);

            if (identityProvider == null)
            {
                _logger.LogError("Identity provider not found for provider {Provider}", stateInfo.Provider);
                return new SocialCallbackResult { ExternalUserData = new AppleUserData() };
            }

            var postData = new Dictionary<string, string>
                {
                    { "code", stateInfo.Code },
                    { "client_id", identityProvider.ClientId },
                    { "client_secret",  GenerateClientSecret(identityProvider)},
                    { "redirect_uri", stateInfo.RedirectUri },
                    { "grant_type", GrantTypes.AuthCode }
                };

            var (response, error) = await _httpService.SendFormUrlEncoded<SocialOauthAccessToken>(HttpMethod.Post, postData, identityProvider.TokenUrl);

            if (!string.IsNullOrWhiteSpace(error) || response == null)
            {
                _logger.LogError("Error while getting access token: {Error}", error);
                return new SocialCallbackResult { ExternalUserData = new AppleUserData() };
            }

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(response.IdToken);
            var payload = JsonConvert.SerializeObject(jwtToken.Payload);
            var deserializeAppleIdToken = JsonConvert.DeserializeObject<AppleIdToken>(payload);
            var appleUserData = new AppleUserData
            {
                Email = deserializeAppleIdToken?.Email,
                ExternalProviderUserId = deserializeAppleIdToken?.ExternalProviderUserId,
                Roles = identityProvider?.InitialRoles ?? [],
                Platform = stateInfo.Provider
            };
            return new SocialCallbackResult
            {
                ExternalUserData = appleUserData,
                AccessToken = response.AccessToken,
                IdToken = response.IdToken,
                RefreshToken = response.RefreshToken
            };
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var callbackResult = await HandleSocialLoginCallback(stateInfo);
            return callbackResult.ExternalUserData;
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
                { "aud", identityProvider.AppleAudience ?? IdpConstants.AppleAuthUrl },
                { "sub", clientId }
            };
            var header = new JwtHeader(signingCredentials);
            var jwt = new JwtSecurityToken(header, payload);
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

    }
}
