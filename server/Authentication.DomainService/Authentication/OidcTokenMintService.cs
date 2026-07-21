using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Iam.DomainService.Utilities;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Pure mint helper extracted from <see cref="AuthorizationCodeExchangeService"/>.
    /// Both the <c>authorization_code</c> grant and the <c>urn:ietf:params:oauth:grant-type:device_code</c>
    /// grant funnel through this service to issue the same access/id/refresh token set plus the
    /// optional IdP session cookie.
    /// </summary>
    public interface IOidcTokenMintService
    {
        Task<OidcTokenMintResult> MintAsync(OidcTokenMintRequest request, CancellationToken ct = default);
    }

    public sealed class OidcTokenMintRequest
    {
        public required User User { get; init; }
        public required string TenantId { get; init; }
        public string? OrganizationId { get; init; }
        public string? Scope { get; init; }
        public string? Nonce { get; init; }
        public string ClientId { get; init; } = string.Empty;
        public List<string>? Amr { get; init; }
        public bool RequireExplicitScope { get; init; } = true;
        public HttpRequest? Request { get; init; }
    }

    public sealed class OidcTokenMintResult
    {
        public string AccessToken { get; init; } = string.Empty;
        public string IdToken { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public string EffectiveTenantId { get; init; } = string.Empty;
        public string Scope { get; init; } = string.Empty;
        public int ExpiresIn { get; init; }
        public DateTime AccessExpiry { get; init; }
        public DateTime RefreshExpiry { get; init; }
        public string? Domain { get; init; }
        public string? CookieDomain { get; init; }
        public bool CanSetCookies => !string.IsNullOrWhiteSpace(Domain);
    }

    public sealed class OidcTokenMintService : IOidcTokenMintService
    {
        private readonly ITokenGenerationService _tokenService;
        private readonly IAuthorizationClaimsResolver _authorizationClaimsResolver;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly IIdpSessionService _idpSessionService;
        private readonly ILogger<OidcTokenMintService> _logger;

        public OidcTokenMintService(
            ITokenGenerationService tokenService,
            IAuthorizationClaimsResolver authorizationClaimsResolver,
            IAuthenticationRepository authenticationRepository,
            ITenants tenants,
            IIdpSessionService idpSessionService,
            ILogger<OidcTokenMintService> logger)
        {
            _tokenService = tokenService;
            _authorizationClaimsResolver = authorizationClaimsResolver;
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
            _idpSessionService = idpSessionService;
            _logger = logger;
        }

        public async Task<OidcTokenMintResult> MintAsync(OidcTokenMintRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (request.User == null)
            {
                throw new ArgumentException("User is required", nameof(request));
            }

            var tenant = _tenants.GetTenantByID(request.TenantId);
            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(
                request.User,
                request.OrganizationId,
                request.Scope,
                requireExplicitScope: request.RequireExplicitScope);

            var tenantAudience = DomainResolver.GetAudience(tenant);

            var fullName = string.Join(' ', new[] { request.User.FirstName, request.User.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            var claims = new OidcClaims
            {
                Sub = request.User.ItemId,
                TenantId = request.TenantId,
                OrgId = request.OrganizationId,
                Nonce = request.Nonce,
                AuthTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ClientId = request.ClientId,
                Audience = tenantAudience,
                Scope = request.Scope,
                Email = request.User.Email,
                Name = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                UserName = request.User.UserName,
                Amr = request.Amr is { Count: > 0 } ? request.Amr : ["pwd"],
                Roles = resolvedClaims.Roles,
                Permissions = resolvedClaims.Permissions
            };

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var accessTokenLifetimeSeconds = Math.Max((authConfiguration?.AccessTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes) * IdpConstants.SecondsPerMinute, IdpConstants.MinAccessTokenLifetimeSeconds);
            var absoluteRefreshTokenLifetimeMinutes = Math.Max(authConfiguration?.AbsoluteRefreshTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes, 1);

            var issuer = DomainResolver.GetIssuer(tenant);

            var idpSessionId = request.Request != null
                ? await _idpSessionService.ResolveOrCreateAsync(
                    request.Request.HttpContext,
                    request.User.ItemId,
                    request.TenantId,
                    OidcRedirectUrlBuilder.GetClientIpAddress(request.Request))
                : string.Empty;

            var idToken = await _tokenService.GenerateIdTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var accessToken = await _tokenService.GenerateAccessTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var refreshTokenModel = await _tokenService.GenerateRefreshTokenAsync(claims, issuer, false, idpSessionId);

            _logger.LogInformation("Tokens issued for user {UserId}, client {ClientId}", request.User.ItemId, request.ClientId);

            var (domain, cookieDomain, isResolved) = request.Request != null
                ? DomainResolver.ResolveDomain(tenant, request.Request)
                : (null, null, false);
            var accessExpiry = DateTime.UtcNow.AddSeconds(accessTokenLifetimeSeconds);
            var refreshExpiry = refreshTokenModel.AbsoluteExpiry == default
                ? DateTime.UtcNow.AddMinutes(absoluteRefreshTokenLifetimeMinutes)
                : refreshTokenModel.AbsoluteExpiry;

            return new OidcTokenMintResult
            {
                AccessToken = accessToken,
                IdToken = idToken,
                RefreshToken = refreshTokenModel.TokenId,
                EffectiveTenantId = request.TenantId,
                Scope = request.Scope ?? string.Empty,
                ExpiresIn = accessTokenLifetimeSeconds,
                AccessExpiry = accessExpiry,
                RefreshExpiry = refreshExpiry,
                Domain = isResolved ? domain : null,
                CookieDomain = cookieDomain
            };
        }
    }
}