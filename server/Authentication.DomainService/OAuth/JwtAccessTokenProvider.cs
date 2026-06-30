using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.OAuth.RequestModel;

namespace Authentication.DomainService.OAuth
{
    public sealed class JwtAccessTokenProvider : IJwtAccessTokenProvider
    {
        private readonly ILogger<JwtAccessTokenProvider> _logger;
        private readonly IDatabase _cacheDb;
        private readonly ICryptoService _cryptoService;
        private readonly ICertificateProviderFactory _certificateProviderFactory;
        private readonly IAuthorizationClaimsResolver _authorizationClaimsResolver;
        private string? _key;


        public JwtAccessTokenProvider(
            ILogger<JwtAccessTokenProvider> logger,
            ICacheClient cacheClient,
            ICryptoService cryptoService,
            ICertificateProviderFactory certificateProviderFactory,
            IAuthorizationClaimsResolver authorizationClaimsResolver
        )
        {
            _logger = logger;
            _cacheDb = cacheClient.CacheDatabase();
            _cryptoService = cryptoService;
            _certificateProviderFactory = certificateProviderFactory;
            _authorizationClaimsResolver = authorizationClaimsResolver;

        }

        public async Task<JwtAccessToken> GetJwtAccessToken(
            IdentityConfiguration authenticationConfiguration,
            Tenant tenant,
            User user,
            TokenRequest tokenRequest,
            StateInfo? state = null,
            IEnumerable<string>? clientAllowedServiceAccessResources = null)
        {
            _key = _cryptoService.Hash(Encoding.UTF8.GetBytes($"{tenant.TenantId}::{tenant.ItemId}"));
            var certificate = await GetOrRetrieveCertAsync(tenant);
            if (certificate == null) return new JwtAccessToken();
            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(user, tokenRequest.OrganizationId, state?.Scope, clientAllowedServiceAccessResources);
            return MapJwtAccessToken(authenticationConfiguration, tenant, user, certificate, resolvedClaims, tokenRequest, stateInfo: state);
        }

        public JwtAccessToken MapJwtAccessToken(IdentityConfiguration authenticationConfiguration, Tenant tenant, User user, byte[] certificate, ResolvedAuthorizationClaims resolvedClaims, TokenRequest tokenRequest, StateInfo? stateInfo = null)
        {
            var jwtAccessToken = new JwtAccessToken
            {
                RefreshTokenValidForNumberMinute = authenticationConfiguration.RefreshTokenValidForNumberMinutes,
                AccessTokenValidForNumberMinute = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                RememberMeRefreshTokenValidForNumberMinute = authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes,
                Issuer = tenant.JwtTokenParameters.Issuer,
                Audience = DomainResolver.GetAudience(tenant),
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddMinutes(authenticationConfiguration.AccessTokenValidForNumberMinutes),
                SigningCredentials = MakeSigningCredentials(certificate, tenant.JwtTokenParameters.PrivateCertificatePassword)
            };

            var claimsIdentity = new ClaimsIdentity("seliseblocks-authentication");
            AddClaims(claimsIdentity, tenant, user, resolvedClaims, tokenRequest, stateInfo: stateInfo);
            jwtAccessToken.Claims = claimsIdentity.Claims;

            return jwtAccessToken;
        }

        public static void AddClaims(ClaimsIdentity claimsIdentity, Tenant tenant, User user, ResolvedAuthorizationClaims resolvedClaims, TokenRequest tokenRequest, StateInfo? stateInfo = null)
        {

            claimsIdentity.AddClaim(new Claim(BlocksContext.SUBJECT_CLAIM, $"blocks|{user.ItemId}"));
            claimsIdentity.AddClaim(new Claim(BlocksContext.USER_ID_CLAIM, user.ItemId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.ISSUED_AT_TIME_CLAIM, EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64));
            var resolvedOrgId = "default";
            if (!string.IsNullOrWhiteSpace(tokenRequest.OrganizationId)
                && user.OrganizationIds.Contains(tokenRequest.OrganizationId))
            {
                resolvedOrgId = tokenRequest.OrganizationId;
            }
            claimsIdentity.AddClaim(new Claim(BlocksContext.ORGANIZATION_ID_CLAIM, resolvedOrgId));
            claimsIdentity.AddClaim(new Claim("token_version", user.TokenVersion.ToString(), ClaimValueTypes.Integer32));
            claimsIdentity.AddClaim(new Claim("security_stamp", user.SecurityStamp ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(stateInfo?.Nonce))
            {
                claimsIdentity.AddClaim(new Claim("nonce", stateInfo?.Nonce ?? ""));
            }

            foreach (var role in resolvedClaims.Roles)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.ROLES_CLAIM, role));
            }

            foreach (var resource in resolvedClaims.Resources)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.SERVICE_ACCESS_CLAIM, resource));
            }

            foreach (var permission in resolvedClaims.Permissions)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.PERMISSION_CLAIM, permission));
            }

            if (tokenRequest.IsImpersonation)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.IMPERSONATED_CLAIM, "true", ClaimValueTypes.Boolean));
                claimsIdentity.AddClaim(new Claim(BlocksContext.ORIGINAL_TENANT_ID_CLAIM, tokenRequest.OriginalTenantId));
                claimsIdentity.AddClaim(new Claim(BlocksContext.TENANT_ID_CLAIM, tokenRequest.TargetTenantId));
                claimsIdentity.AddClaim(new Claim(BlocksContext.IMPERSONATION_SESSION_ID_CLAIM, tokenRequest.ImpersonationSessionId ?? string.Empty));
            }
            else
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.TENANT_ID_CLAIM, tenant.TenantId));
            }
        }

        public async Task<byte[]?> GetOrRetrieveCertAsync(Tenant tenant)
        {
            _logger.LogInformation("Getting Certificate");
            var cachedCert = _cacheDb.StringGet(_key);
            _logger.LogInformation("Has Cache Certificate: {CC}", cachedCert.HasValue);

            if (!cachedCert.HasValue)
            {
                var provider = _certificateProviderFactory.GetProvider(tenant.JwtTokenParameters?.CertificateStorageType ?? CertificateStorageType.Azure);
                var certificate = await provider.GetCertificateAsync(_key);

                if (certificate != null && certificate.Length > 0)
                {
                    var expirationDays = tenant.JwtTokenParameters?.CertificateValidForNumberOfDays - (DateTime.UtcNow - tenant.JwtTokenParameters?.IssueDate)?.Days - 1;
                    _cacheDb.StringSet(_key, certificate, TimeSpan.FromDays(expirationDays ?? 0));
                    _logger.LogInformation("Certificate set to cache");
                }
                return certificate;
            }

            return cachedCert;
        }

        public static SigningCredentials MakeSigningCredentials(byte[] certificateData, string password)
        {
            X509Certificate2 certificate;

            try
            {
                certificate = X509CertificateLoader.LoadPkcs12(certificateData, password, X509KeyStorageFlags.Exportable);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PKCS12 certificate loading failed: {ex.Message}. Trying fallback loader...");

                try
                {
                    certificate = X509CertificateLoader.LoadCertificate(certificateData);
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"Fallback certificate loading failed: {fallbackEx.Message}");
                    throw new InvalidOperationException("Failed to load X509 certificate from provided data.", fallbackEx);
                }
            }

            var rsa = certificate.GetRSAPrivateKey() ?? throw new CryptographicException("Invalid private key");
            // Create the security key from the full RSA key parameters
            _ = new RsaSecurityKey(rsa)
            {
                // *** THIS IS THE CRITICAL STEP ***
                // Set the KeyId on the key object using the same logic as the JWKS endpoint.
                KeyId = Base64UrlEncoder.Encode(certificate.Thumbprint)
            };
            return new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256, SecurityAlgorithms.Sha256Digest);
        }

    }
}
