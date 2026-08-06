using Authentication.DomainService.Dtos;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Iam.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json; // required by other files

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Handles the OAuth 2.0 <c>refresh_token</c> grant for the OIDC token endpoint:
    /// resolves the refresh token from cookies or form, delegates rotation to
    /// <see cref="RefreshTokenAuthenticationService"/>, and writes cookies (or returns tokens in body).
    /// Extracted from <c>AuthorizationFlowService.RotateRefreshToken</c>.
    /// </summary>
    public sealed class OidcRefreshTokenService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ILogger<OidcRefreshTokenService> _logger;

        public OidcRefreshTokenService(
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            ITenants tenants,
            RefreshTokenAuthenticationService refreshTokenAuthenticationService,
            IAuthenticationService authenticationService,
            IRefreshTokenRepository refreshTokenRepository,
            ILogger<OidcRefreshTokenService> logger)
        {
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _tenants = tenants;
            _refreshTokenAuthenticationService = refreshTokenAuthenticationService;
            _authenticationService = authenticationService;
            _refreshTokenRepository = refreshTokenRepository;
            _logger = logger;
        }

        public async Task<IActionResult> RotateAsync(HttpRequest request)
        {
            var clientId = request.Form["client_id"].ToString();
            if (string.IsNullOrEmpty(clientId))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "Missing client_id" });
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            if (client is null)
            {
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "client not found" });
            }

            var refreshToken = await ResolveRefreshTokenFromRequestAsync(client, request);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "refresh token not found" });
            }

            var (validation, configuration, tokenCache, user) = await ValidateRefreshTokenAsync(refreshToken);
            if (validation != null)
            {
                return validation;
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.RefreshToken,
                OrganizationId = string.IsNullOrWhiteSpace(tokenCache!.OrganizationId) ? "default" : tokenCache.OrganizationId,
                ClientId = tokenCache.ClientId,
                RefreshToken = refreshToken,
                Request = request
            };

            var response = await _refreshTokenAuthenticationService.AuthenticateAsync(tokenRequest, configuration!, user!);

            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                var statusCode = response.StatusCode > 0 ? response.StatusCode : StatusCodes.Status400BadRequest;
                return new ObjectResult(new
                {
                    error = response.Error,
                    error_description = response.ErrorDescription
                })
                {
                    StatusCode = statusCode
                };
            }

            return await BuildRefreshTokenResponseAsync(client, tokenCache, response, request);
        }

        private async Task<string> ResolveRefreshTokenFromRequestAsync(OidcClientRegistration client, HttpRequest request)
        {
            if (!client.UseTokensCookie)
            {
                return request.Form["refresh_token"].ToString();
            }

            var bc = BlocksContext.GetContext();
            var tenant = _tenants.GetTenantByID(bc?.TenantId ?? "default");
            var (domain, _, isResolved) = DomainResolver.ResolveDomain(tenant, request);
            var cookieKey = isResolved && !string.IsNullOrWhiteSpace(domain)
                ? $"{IdpConstants.RefreshTokenCookieName}_{domain}"
                : string.Empty;

            if (string.IsNullOrWhiteSpace(cookieKey))
            {
                return request.Form["refresh_token"].ToString();
            }

            var cookieToken = request.HttpContext.Request.Cookies[cookieKey];
            if (!string.IsNullOrWhiteSpace(cookieToken))
            {
                return cookieToken;
            }

            // For API/postman callers (or unresolved domain), accept body token as runtime fallback.
            return request.Form["refresh_token"].ToString();
        }

        private async Task<(IActionResult? Error, IdentityConfiguration? Configuration, RefreshTokenCache? TokenCache, User? User)> ValidateRefreshTokenAsync(string refreshToken)
        {
            var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return (new BadRequestObjectResult(new { error = OAuthError.AuthConfigMissing }), null, null, null);
            }

            var cachedRefreshToken = await _cacheClient.GetStringValueAsync(refreshToken);
            RefreshTokenCache? tokenCache;
            if (string.IsNullOrWhiteSpace(cachedRefreshToken))
            {
                // Redis only holds a 30-min sliding window; Mongo is the system of record with the
                // real (default 7-day) AbsoluteExpiry. A session idle past the sliding window but
                // still within its absolute lifetime must still be able to refresh.
                tokenCache = await RehydrateFromPersistedTokenAsync(refreshToken, configuration);
            }
            else
            {
                tokenCache = JsonSerializer.Deserialize<RefreshTokenCache>(cachedRefreshToken);
            }

            if (tokenCache == null || string.IsNullOrWhiteSpace(tokenCache.UserId))
            {
                return (new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Refresh token is invalid or expired" }), null, null, null);
            }

            if (string.IsNullOrWhiteSpace(tokenCache.ClientId) || await _authenticationRepository.GetOidcClientRegistrationAsync(tokenCache.ClientId) == null)
            {
                return (new UnauthorizedObjectResult(new { error = "invalid_client", error_description = "Client configuration not found" }), null, null, null);
            }

            var currentTenantId = BlocksContext.GetContext()?.TenantId;
            if (!string.Equals(tokenCache.TenantId, currentTenantId, StringComparison.OrdinalIgnoreCase))
            {
                return (new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Refresh token tenant mismatch" }), null, null, null);
            }

            var user = await _authenticationRepository.GetUserByIdAsync(tokenCache.UserId);
            if (user == null)
            {
                return (new UnauthorizedObjectResult(new { error = "invalid_user" }), null, null, null);
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                return (new ObjectResult(new
                {
                    error = OAuthError.AccountLocked,
                    error_description = "Account is temporarily locked due to failed authentication attempts"
                })
                {
                    StatusCode = StatusCodes.Status423Locked
                }, null, null, null);
            }

            return (null, configuration, tokenCache, user);
        }

        private async Task<RefreshTokenCache?> RehydrateFromPersistedTokenAsync(string refreshToken, IdentityConfiguration configuration)
        {
            var persisted = await _refreshTokenRepository.GetByTokenIdAsync(refreshToken);
            if (persisted == null || persisted.IsRevoked || persisted.AbsoluteExpiry <= DateTime.UtcNow)
            {
                return null;
            }

            var tokenCache = new RefreshTokenCache
            {
                RefreshToken = persisted.TokenId,
                TenantId = persisted.TenantId,
                OrganizationId = persisted.OrganizationId,
                ClientId = persisted.ClientId,
                SessionId = persisted.SessionId ?? string.Empty,
                IssuedUtc = persisted.IssuedUtc,
                ExpiresUtc = persisted.SlidingExpiry,
                AbsoluteExpiresUtc = persisted.AbsoluteExpiry,
                UserId = persisted.UserId,
                IpAddresses = persisted.IpAddress,
                Scope = persisted.Scope,
                Impersonated = persisted.Impersonated,
                ImpersonationId = persisted.ImpersonationId
            };

            var slidingSeconds = configuration.RefreshTokenValidForNumberMinutes > 0
                ? configuration.RefreshTokenValidForNumberMinutes * 60
                : 15 * 60;
            var remainingAbsoluteSeconds = (int)Math.Max(1, (persisted.AbsoluteExpiry - DateTime.UtcNow).TotalSeconds);
            var ttlSeconds = Math.Min(slidingSeconds, remainingAbsoluteSeconds);

            await _cacheClient.AddStringValueAsync(refreshToken, JsonSerializer.Serialize(tokenCache), ttlSeconds);

            return tokenCache;
        }

        private async Task<IActionResult> BuildRefreshTokenResponseAsync(
            OidcClientRegistration client,
            RefreshTokenCache tokenCache,
            TokenResponse response,
            HttpRequest request)
        {
            if (tokenCache.Impersonated && !string.IsNullOrWhiteSpace(tokenCache.ImpersonationId))
            {
                var existingSession = await _authenticationRepository.GetImpersonationSessionByIdAsync(tokenCache.ImpersonationId);
                return await _authenticationService.ExecuteImpersonateAsync(
                    new ImpersonateRequest
                    {
                        TargetTenantId = existingSession.TargetTenantId,
                        OrganizationId = tokenCache.OrganizationId,
                        ImpersonationId = tokenCache.ImpersonationId,
                        ImpersontingUserId = tokenCache.UserId,
                        RefreshToken = response.RefreshToken
                    },
                    request.HttpContext.Request,
                    request.HttpContext.Response
                );
            }

            if (client.UseTokensCookie)
            {
                var tenantId = BlocksContext.GetContext()?.TenantId ?? "default";
                var resolvedTenant = _tenants.GetTenantByID(tenantId);
                var (domain, _, _) = DomainResolver.ResolveDomain(resolvedTenant, request);
                var cookiesSet = CookieHelper.AppendCookies(response, request.HttpContext.Response, domain);
                if (cookiesSet)
                {
                    return new OkObjectResult(TokenResponsePayload.Build(response, cookiesSet: true));
                }
            }

            return new OkObjectResult(TokenResponsePayload.Build(response, cookiesSet: false));
        }
    }
}
