using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Authentication.DomainService.Utilities;

namespace Authentication.DomainService.OAuth.Services
{
    public sealed class ClientCredentialAuthorizationService : ITokenService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICertificateProviderFactory _certificateProviderFactory;
        private readonly ICryptoService _cryptoService;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        
        public ClientCredentialAuthorizationService(IAuthenticationRepository authenticationRepository,
                                                    ICertificateProviderFactory certificateProviderFactory,
                                                    ICryptoService cryptoService,
                                                    ICacheClient cacheClient,
                                                    ITenants tenants)
        {
            _authenticationRepository = authenticationRepository;
            _certificateProviderFactory = certificateProviderFactory;
            _cryptoService = cryptoService;
            _cacheClient = cacheClient;
            _tenants = tenants;    
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, IdentityConfiguration authenticationConfiguration, User? user = null)
        {
            if (authenticationConfiguration == null)
                return new TokenResponse
                {
                    Error = "server_error",
                    ErrorDescription = "Authentication configuration missing"
                };

            var client = await _authenticationRepository.GetClientCredentialByIdAsync(request.ClientId);
            var validationResult = ValidateClient(client, request);

            if (validationResult != null)
                return validationResult;

            var jwtToken = await GetJwtAccessToken(authenticationConfiguration, client!);
            if (jwtToken == null)
                return new TokenResponse
                {
                    Error = "server_error",
                    ErrorDescription = "Unable to resolve tenant or signing certificate"
                };

            var accessToken = OAuthJwtAccessTokenManager.CreateJwtAccessToken(jwtToken);

            var lifetimeMinutes = client!.AccessTokenValidForNumberMinutes > 0
                ? client.AccessTokenValidForNumberMinutes
                : authenticationConfiguration.AccessTokenValidForNumberMinutes;

            return new TokenResponse
            {
                AccessToken = accessToken,
                ExpiresIn = lifetimeMinutes,
                ExpiresUtc = jwtToken.Expires,
                TokenType = "Bearer",
                StatusCode = 200
            };
        }

        private async Task<JwtAccessToken?> GetJwtAccessToken(
            IdentityConfiguration authenticationConfiguration,
            ClientCredential client)
        {
            var tenant = _tenants.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? "");
            if (tenant == null) return null;
            var certificate = await RetrievePrivateCertAsync(tenant);
            if (certificate == null || certificate.Length == 0) return null;
            return MapJwtAccessToken(authenticationConfiguration, tenant, client, certificate);
        }

        public async Task<byte[]?> RetrievePrivateCertAsync(Tenant tenant)
        {
            var _key = _cryptoService.Hash(Encoding.UTF8.GetBytes($"{tenant.TenantId}::{tenant.ItemId}"));
            var cachedCert = _cacheClient.CacheDatabase().StringGet(_key);

            if (!cachedCert.HasValue)
            {
                var provider = _certificateProviderFactory.GetProvider(tenant.JwtTokenParameters?.CertificateStorageType ?? CertificateStorageType.Azure);
                var certificate = await provider.GetCertificateAsync(_key);

                if (certificate.Length > 0)
                {
                    var expirationDays = tenant.JwtTokenParameters?.CertificateValidForNumberOfDays - (DateTime.UtcNow - tenant.JwtTokenParameters?.IssueDate)?.Days - 1;
                    _cacheClient.CacheDatabase().StringSet(_key, certificate, TimeSpan.FromDays(expirationDays ?? 0));
                }

                return certificate;
            }

            return cachedCert;
        }

        private static JwtAccessToken MapJwtAccessToken(
            IdentityConfiguration authenticationConfiguration,
            Tenant tenant,
            ClientCredential client,
            byte[] certificate)
        {
            var lifetimeMinutes = client.AccessTokenValidForNumberMinutes > 0
                ? client.AccessTokenValidForNumberMinutes
                : authenticationConfiguration.AccessTokenValidForNumberMinutes;

            var jwtAccessToken = new JwtAccessToken
            {
                AccessTokenValidForNumberMinute = lifetimeMinutes,
                Issuer = tenant.JwtTokenParameters.Issuer,
                Audience = DomainResolver.GetAudience(tenant),
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddMinutes(lifetimeMinutes),
                SigningCredentials = JwtAccessTokenProvider.MakeSigningCredentials(certificate, tenant.JwtTokenParameters.PrivateCertificatePassword)
            };

            var claimsIdentity = new ClaimsIdentity("seliseblocks-authentication");
            AddClaims(claimsIdentity, tenant, client);
            jwtAccessToken.Claims = claimsIdentity.Claims;

            return jwtAccessToken;
        }

        public static void AddClaims(
            ClaimsIdentity claimsIdentity,
            Tenant tenant,
            ClientCredential client)
        {
            claimsIdentity.AddClaim(new Claim(BlocksContext.TENANT_ID_CLAIM, tenant.TenantId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.SUBJECT_CLAIM, $"blocks|{client.ItemId}"));
            claimsIdentity.AddClaim(new Claim("client_id", client.ItemId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.ORGANIZATION_ID_CLAIM, client.OrganizationId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.ISSUED_AT_TIME_CLAIM, EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64));

            foreach (var role in client.Roles)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.ROLES_CLAIM, role));
            }

            foreach (var permission in client.Permissions)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.PERMISSION_CLAIM, permission));
            }
        }

        private static TokenResponse? ValidateClient(ClientCredential? client, TokenRequest request)
        {
            return client switch
            {
                null => new TokenResponse
                {
                    Error = "invalid_client",
                    ErrorDescription = "No client found"
                },

                _ when !SecretsMatch(request.ClientSecret, client.ClientSecret) => new TokenResponse
                {
                    Error = "invalid_client",
                    ErrorDescription = "Client secret not match"
                },

                _ when !client.IsActive => new TokenResponse
                {
                    Error = "invalid_client",
                    ErrorDescription = "Client is not active"
                },

                _ => null
            };
        }

        private static bool SecretsMatch(string? requestSecret, string? clientSecret)
        {
            if (string.IsNullOrEmpty(requestSecret) || string.IsNullOrEmpty(clientSecret))
            {
                return false;
            }

            var requestBytes = Encoding.UTF8.GetBytes(requestSecret);
            var clientBytes = Encoding.UTF8.GetBytes(clientSecret);
            return CryptographicOperations.FixedTimeEquals(requestBytes, clientBytes);
        }

    }
}
