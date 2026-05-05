using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Authentication.DomainService.Utilities;

namespace Authentication.DomainService.OAuth
{
    public class JwtAccessTokenProvider : IJwtAccessTokenProvider
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

        public async Task<JwtAccessToken> GetJwtAccessToken(AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user, StateInfo? state = null, string? organizationId = null, TokenIssuanceContext? issuanceContext = null)
        {
            _key = _cryptoService.Hash(Encoding.UTF8.GetBytes($"{tenant.TenantId}::{tenant.ItemId}"));
            var certificate = await GetOrRetrieveCertAsync(tenant);
            if (certificate == null) return new JwtAccessToken();
            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(user, organizationId, state?.Scope);
            return MapJwtAccessToken(authenticationConfiguration, tenant, user, certificate, resolvedClaims, stateInfo: state, organizationId: organizationId, issuanceContext: issuanceContext);
        }

        public JwtAccessToken MapJwtAccessToken(AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user, byte[] certificate, ResolvedAuthorizationClaims resolvedClaims, StateInfo? stateInfo = null, string? organizationId = null, TokenIssuanceContext? issuanceContext = null)
        {
            var jwtAccessToken = new JwtAccessToken
            {
                RefreshTokenValidForNumberMinute = authenticationConfiguration.RefreshTokenValidForNumberMinutes,
                AccessTokenValidForNumberMinute = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                RememberMeRefreshTokenValidForNumberMinute = authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes,
                Issuer = tenant.JwtTokenParameters.Issuer,
                Audience = TenantDomainPolicy.GetAudience(tenant),
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddMinutes(authenticationConfiguration.AccessTokenValidForNumberMinutes),
                SigningCredentials = MakeSigningCredentials(certificate, tenant.JwtTokenParameters.PrivateCertificatePassword)
            };

            var claimsIdentity = new ClaimsIdentity("seliseblocks-authentication");
            AddClaims(claimsIdentity, tenant, user, resolvedClaims, stateInfo: stateInfo, organizationId: organizationId, issuanceContext: issuanceContext);
            jwtAccessToken.Claims = claimsIdentity.Claims;

            return jwtAccessToken;
        }

        public static void AddClaims(ClaimsIdentity claimsIdentity, Tenant tenant, User user, ResolvedAuthorizationClaims resolvedClaims, StateInfo? stateInfo = null, string? organizationId = null, TokenIssuanceContext? issuanceContext = null)
        {
            claimsIdentity.AddClaim(new Claim(BlocksContext.TENANT_ID_CLAIM, tenant.TenantId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.SUBJECT_CLAIM, $"blocks|{user.ItemId}"));
            claimsIdentity.AddClaim(new Claim(BlocksContext.USER_ID_CLAIM, user.ItemId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.ISSUED_AT_TIME_CLAIM, EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64));
            var resolvedOrgId = "default";
            if (!string.IsNullOrWhiteSpace(organizationId)
                && (user.Roles.ContainsKey(organizationId)
                    || user.Permissions.ContainsKey(organizationId)
                    || user.OrganizationIds.Contains(organizationId)))
            {
                resolvedOrgId = organizationId;
            }
            claimsIdentity.AddClaim(new Claim(BlocksContext.ORGANIZATION_ID_CLAIM, resolvedOrgId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.EMAIL_CLAIM, user.Email ?? string.Empty));
            claimsIdentity.AddClaim(new Claim(BlocksContext.USER_NAME_CLAIM, user.UserName ?? string.Empty));
            claimsIdentity.AddClaim(new Claim(BlocksContext.DISPLAY_NAME_CLAIM, $"{user.FirstName ?? string.Empty} {user.LastName ?? string.Empty}".Trim()));
            claimsIdentity.AddClaim(new Claim(BlocksContext.PHONE_NUMBER_CLAIM, user.PhoneNumber ?? string.Empty));
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
                claimsIdentity.AddClaim(new Claim("service_access", resource));
            }

            foreach (var permission in resolvedClaims.Permissions)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.PERMISSION_CLAIM, permission));
            }

            if (issuanceContext?.IsImpersonation == true)
            {
                claimsIdentity.AddClaim(new Claim("impersonated", "true", ClaimValueTypes.Boolean));
                if (!string.IsNullOrWhiteSpace(issuanceContext.OriginalTenantId))
                {
                    claimsIdentity.AddClaim(new Claim("orig_tenant", issuanceContext.OriginalTenantId));
                }

                if (!string.IsNullOrWhiteSpace(issuanceContext.ActorUserId))
                {
                    var actPayload = System.Text.Json.JsonSerializer.Serialize(new { sub = issuanceContext.ActorUserId });
                    claimsIdentity.AddClaim(new Claim("act", actPayload, "JSON"));
                }
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
