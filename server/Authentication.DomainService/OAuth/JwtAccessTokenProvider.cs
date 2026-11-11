using Blocks.Genesis;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Entities;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Iam.DomainService.Utilities;
using Iam.DomainService.Resources;
using Authentication.DomainService.OAuth.RequestModel;
using Blocks.Genesis.Entities.Iam;
using Authentication.DomainService.OAuth.ResponseModel;
using Microsoft.AspNetCore.Http;

namespace Authentication.DomainService.OAuth
{
    /// <summary>The resolved roles/permissions of a token together with the organization scope they were read from.</summary>
    public sealed record TokenClaimSet(ResolvedAuthorizationClaims ResolvedClaims, OrganizationScope OrganizationScope);

    public sealed class JwtAccessTokenProvider : IJwtAccessTokenProvider
    {
        /// <summary>The organization status claim, <c>"disabled"</c> on the all-disabled "no-org" token only (spec #668 §6.4).</summary>
        public const string OrganizationStatusClaim = "org_status";

        private readonly ILogger<JwtAccessTokenProvider> _logger;
        private readonly IDatabase _cacheDb;
        private readonly ICryptoService _cryptoService;
        private readonly ICertificateProviderFactory _certificateProviderFactory;
        private readonly IAuthorizationClaimsResolver _authorizationClaimsResolver;
        private readonly IResourceRepository _resourceRepository;


        public JwtAccessTokenProvider(
            ILogger<JwtAccessTokenProvider> logger,
            ICacheClient cacheClient,
            ICryptoService cryptoService,
            ICertificateProviderFactory certificateProviderFactory,
            IAuthorizationClaimsResolver authorizationClaimsResolver,
            IResourceRepository resourceRepository
        )
        {
            _logger = logger;
            _cacheDb = cacheClient.CacheDatabase();
            _cryptoService = cryptoService;
            _certificateProviderFactory = certificateProviderFactory;
            _authorizationClaimsResolver = authorizationClaimsResolver;
            _resourceRepository = resourceRepository;

        }

        public async Task<JwtAccessToken> GetJwtAccessToken(
            IdentityConfiguration authenticationConfiguration,
            Tenant tenant,
            User user,
            TokenRequest tokenRequest,
            StateInfo? state = null)
        {
            // A local, not a field: this provider is a singleton, so a field would be shared by
            // concurrent requests and one tenant's mint could read another tenant's key.
            var certificateKey = _cryptoService.Hash(Encoding.UTF8.GetBytes($"{tenant.TenantId}::{tenant.ItemId}"));
            var certificate = await GetOrRetrieveCertAsync(certificateKey, tenant);
            if (certificate == null) return new JwtAccessToken();

            // The authoritative organization decision for every OAuth-family token: password,
            // social, mfa, embedded login, refresh and token exchange all arrive here. The sign-in
            // legs only pre-fill TokenRequest.OrganizationId; it is re-validated against current
            // membership and organization status, so a stale, unauthorised or disabled value cannot
            // survive into a token. Callers that can answer a refusal decide first (ManageTokenAsync,
            // the OIDC refresh and token exchange); this is the fail-closed backstop for any other.
            if (tokenRequest.OrganizationDecision is null && await ResolveOrganizationAsync(user, tokenRequest) is { } refusal)
            {
                throw new InvalidOperationException($"No token may be minted: {refusal.Error}.");
            }

            var decision = tokenRequest.OrganizationDecision!;

            // Roles and permissions are read out of the SAME scope that becomes the claim. Passing
            // the requested organization here instead would mint a token whose claim and whose
            // role set disagree.
            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(user, decision.Scope.ClaimValue, state?.Scope);
            return MapJwtAccessToken(authenticationConfiguration, tenant, user, certificate, new TokenClaimSet(resolvedClaims, decision.Scope), tokenRequest, stateInfo: state);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reads the tenant's multi-organization mode and the all-disabled setting from the one tenant-configuration
        /// read the mint already did; a configuration that cannot be read is treated as multi-org disabled, which
        /// resolves to "default" and preserves the behavior every caller had before the three-state claim existed.
        /// Failing the other way would drop every caller into "no-org" on a transient read failure.
        /// </remarks>
        public async Task<TokenResponse?> ResolveOrganizationAsync(User user, TokenRequest tokenRequest)
        {
            var settings = await OrganizationAccessResolver.ReadIssuanceSettingsAsync(_resourceRepository, _logger, user.ItemId);

            // Impersonation (start, switch, stop and its refresh) is not gated: it resolves exactly as before,
            // including the organization its refresh token remembers.
            if (IsImpersonationIssuance(tokenRequest))
            {
                tokenRequest.OrganizationDecision = OrganizationScopeResolver.Issued(
                    OrganizationAccessResolver.Resolve(user, tokenRequest.OrganizationId, settings.IsMultiOrgEnabled));
                return null;
            }

            var decision = await OrganizationAccessResolver.ResolveForIssuanceAsync(
                _resourceRepository,
                _logger,
                user,
                tokenRequest.OrganizationId,
                isExplicit: tokenRequest.OrganizationSource != OrganizationRequestSource.ServerFilled,
                isLinkOrganization: tokenRequest.OrganizationSource == OrganizationRequestSource.Link,
                settings);

            if (!decision.IsIssued)
            {
                return OrganizationRefusal(decision.Outcome);
            }

            tokenRequest.OrganizationDecision = decision;
            tokenRequest.OrganizationId = decision.Scope.ClaimValue;
            return null;
        }

        /// <summary>The 403/503 response of a refused issuance (spec #668 §6.2). The body never names the organization.</summary>
        public static TokenResponse OrganizationRefusal(OrganizationIssuanceOutcome outcome) =>
            outcome == OrganizationIssuanceOutcome.StatusUnavailable
                ? new TokenResponse
                {
                    Error = OAuthError.TemporarilyUnavailable,
                    ErrorDescription = OAuthError.OrganizationStatusUnavailableDescription,
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                }
                : new TokenResponse
                {
                    Error = OAuthError.OrganizationDisabled,
                    ErrorDescription = OAuthError.OrganizationDisabledDescription,
                    StatusCode = StatusCodes.Status403Forbidden
                };

        private static bool IsImpersonationIssuance(TokenRequest tokenRequest) =>
            tokenRequest.IsImpersonation
            || string.Equals(tokenRequest.GrantType, GrantTypes.ImpersonationCloud, StringComparison.OrdinalIgnoreCase);

        public JwtAccessToken MapJwtAccessToken(IdentityConfiguration authenticationConfiguration, Tenant tenant, User user, byte[] certificate, TokenClaimSet claimSet, TokenRequest tokenRequest, StateInfo? stateInfo = null)
        {
            var resolvedClaims = claimSet.ResolvedClaims;
            var organizationScope = claimSet.OrganizationScope;
            var effectiveClaims = resolvedClaims;
            if (tokenRequest.IsLinkAuthentication)
            {
                effectiveClaims = new ResolvedAuthorizationClaims
                {
                    Roles = IntersectClaims(resolvedClaims.Roles, tokenRequest.RestrictedRoles),
                    Permissions = IntersectClaims(resolvedClaims.Permissions, tokenRequest.RestrictedPermissions)
                };
            }

            // Every token carries the tenant audience, link-authenticated or not.
            //
            // A link token used to get aud = the link's client (SPEC23 H4), to make it weaker
            // than a full session. In practice that made it unusable rather than weaker: the
            // tenant audience is what every Blocks API validates against, so the token was
            // rejected by all of them -- including the iam/me call a construct makes to
            // establish its session, which is the whole point of redeeming the link.
            //
            // What actually bounds a link token is the role and permission intersection below
            // and amr=link, and neither depends on the audience.
            var audience = DomainResolver.GetAudience(tenant);

            var jwtAccessToken = new JwtAccessToken
            {
                RefreshTokenValidForNumberMinute = authenticationConfiguration.RefreshTokenValidForNumberMinutes,
                AccessTokenValidForNumberMinute = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                RememberMeRefreshTokenValidForNumberMinute = authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes,
                Issuer = tenant.JwtTokenParameters.Issuer,
                Audience = audience,
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddMinutes(authenticationConfiguration.AccessTokenValidForNumberMinutes),
                SigningCredentials = MakeSigningCredentials(certificate, tenant.JwtTokenParameters.PrivateCertificatePassword)
            };

            var claimsIdentity = new ClaimsIdentity("seliseblocks-authentication");
            AddClaims(claimsIdentity, tenant, user, effectiveClaims, tokenRequest, organizationScope, stateInfo: stateInfo);
            jwtAccessToken.Claims = claimsIdentity.Claims;

            return jwtAccessToken;
        }

        public static void AddClaims(ClaimsIdentity claimsIdentity, Tenant tenant, User user, ResolvedAuthorizationClaims resolvedClaims, TokenRequest tokenRequest, OrganizationScope organizationScope, StateInfo? stateInfo = null)
        {

            claimsIdentity.AddClaim(new Claim(BlocksContext.SUBJECT_CLAIM, $"blocks|{user.ItemId}"));
            claimsIdentity.AddClaim(new Claim(BlocksContext.USER_ID_CLAIM, user.ItemId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.ISSUED_AT_TIME_CLAIM, EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64));
            // Decided by OrganizationScopeResolver, the one place that owns the three-state contract
            // ("default" / <orgId> / "no-org"). This used to default to "default" and re-run the
            // membership test inline, which meant an unresolvable organization silently became the
            // tenant-wide scope -- the most privileged one there is. Impersonation is unaffected:
            // the cloud user matches none of the membership tests, and a single-organization tenant
            // short-circuits to "default", so both still resolve exactly as before.
            claimsIdentity.AddClaim(new Claim(BlocksContext.ORGANIZATION_ID_CLAIM, organizationScope.ClaimValue));
            // Only on the all-disabled "no-org" token (spec #668 D3), so apps show the disabled message rather
            // than new-user onboarding. Absent on every other token.
            var organizationStatus = tokenRequest.OrganizationDecision?.OrganizationStatus;
            if (!string.IsNullOrWhiteSpace(organizationStatus))
            {
                claimsIdentity.AddClaim(new Claim(OrganizationStatusClaim, organizationStatus));
            }

            claimsIdentity.AddClaim(new Claim("token_version", user.TokenVersion.ToString(), ClaimValueTypes.Integer32));
            claimsIdentity.AddClaim(new Claim("security_stamp", user.SecurityStamp ?? string.Empty));

            // The IdP session. Omitted when absent rather than emitted empty -- unlike
            // impersonation_session_id below, which writes "" and leaves consumers unable to
            // distinguish "no session" from "session unknown".
            if (!string.IsNullOrWhiteSpace(tokenRequest.IdpSessionId))
            {
                // Literal rather than BlocksContext.SESSION_ID_CLAIM: that constant lands in the
                // next Genesis release, and IAM still builds against the published 4.2.0.
                claimsIdentity.AddClaim(new Claim("sid", tokenRequest.IdpSessionId));
            }

            if (!string.IsNullOrWhiteSpace(stateInfo?.Nonce))
            {
                claimsIdentity.AddClaim(new Claim("nonce", stateInfo?.Nonce ?? ""));
            }

            foreach (var role in resolvedClaims.Roles)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.ROLES_CLAIM, role));
            }

            foreach (var permission in resolvedClaims.Permissions)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.PERMISSION_CLAIM, permission));
            }

            if (tokenRequest.IsLinkAuthentication)
            {
                var amrValues = tokenRequest.Amr is { Count: > 0 } ? tokenRequest.Amr : new List<string> { "link" };
                foreach (var amr in amrValues.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    claimsIdentity.AddClaim(new Claim("amr", amr));
                }
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

        public async Task<byte[]?> GetOrRetrieveCertAsync(string certificateKey, Tenant tenant)
        {
            _logger.LogInformation("Getting Certificate");
            var cachedCert = await _cacheDb.StringGetAsync(certificateKey);
            _logger.LogInformation("Has Cache Certificate: {CC}", cachedCert.HasValue);

            if (!cachedCert.HasValue)
            {
                var provider = _certificateProviderFactory.GetProvider(tenant.JwtTokenParameters?.CertificateStorageType ?? CertificateStorageType.Azure);
                var certificate = await provider.GetCertificateAsync(certificateKey);

                if (certificate != null && certificate.Length > 0)
                {
                    var expirationDays = tenant.JwtTokenParameters?.CertificateValidForNumberOfDays - (DateTime.UtcNow - tenant.JwtTokenParameters?.IssueDate)?.Days - 1;
                    await _cacheDb.StringSetAsync(certificateKey, certificate, TimeSpan.FromDays(expirationDays ?? 0));
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


        private static List<string> IntersectClaims(IEnumerable<string>? left, IEnumerable<string>? right)
        {
            var rightSet = new HashSet<string>(right ?? [], StringComparer.OrdinalIgnoreCase);
            return (left ?? [])
                .Where(r => rightSet.Contains(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
