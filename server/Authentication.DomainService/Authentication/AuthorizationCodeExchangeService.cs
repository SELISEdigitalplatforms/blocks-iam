using Authentication.DomainService.Entities;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Iam.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Handles the OAuth 2.0 <c>authorization_code</c> grant for the OIDC token endpoint:
    /// validates the authorization code + PKCE verifier, mints the access/id/refresh token set,
    /// and writes the authentication cookies (or returns tokens in the body as a fallback).
    /// Extracted from <c>AuthorizationFlowService</c>.
    /// </summary>
    public sealed class AuthorizationCodeExchangeService
    {
        private readonly IAuthorizationCodeRepository _authCodeRepo;
        private readonly IRefreshTokenRepository _refreshTokenRepo;
        private readonly ITokenGenerationService _tokenService;
        private readonly IPkceService _pkceService;
        private readonly IUserRepository _userRepository;
        private readonly IAuthorizationClaimsResolver _authorizationClaimsResolver;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly IIdpSessionService _idpSessionService;
        private readonly ILogger<AuthorizationCodeExchangeService> _logger;

        public AuthorizationCodeExchangeService(
            IAuthorizationCodeRepository authCodeRepo,
            IRefreshTokenRepository refreshTokenRepo,
            ITokenGenerationService tokenService,
            IPkceService pkceService,
            IUserRepository userRepository,
            IAuthorizationClaimsResolver authorizationClaimsResolver,
            IAuthenticationRepository authenticationRepository,
            ITenants tenants,
            IIdpSessionService idpSessionService,
            ILogger<AuthorizationCodeExchangeService> logger)
        {
            _authCodeRepo = authCodeRepo;
            _refreshTokenRepo = refreshTokenRepo;
            _tokenService = tokenService;
            _pkceService = pkceService;
            _userRepository = userRepository;
            _authorizationClaimsResolver = authorizationClaimsResolver;
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
            _idpSessionService = idpSessionService;
            _logger = logger;
        }

        public async Task<IActionResult> ExchangeAsync(HttpRequest request)
        {
            var code = request.Form["code"].ToString();
            var codeVerifier = request.Form["code_verifier"].ToString();
            var clientId = request.Form["client_id"].ToString();
            var redirectUri = request.Form["redirect_uri"].ToString();

            if (string.IsNullOrWhiteSpace(clientId))
            {
                OidcRedirectUrlBuilder.TryReadBasicClientAuthentication(request, out clientId, out _);
            }

            // Tenant ID resolution: form > query > header (X-Blocks-Key)
            var tenantId = !string.IsNullOrWhiteSpace(request.Form["tenant_id"].ToString())
                ? request.Form["tenant_id"].ToString()
                : (!string.IsNullOrWhiteSpace(request.Query["tenant_id"].ToString())
                    ? request.Query["tenant_id"].ToString()
                    : (request.Headers.TryGetValue("X-Blocks-Key", out var headerValue)
                        ? headerValue.ToString()
                        : string.Empty));

            return await ExchangeCoreAsync(code, codeVerifier, clientId, redirectUri, tenantId, request, request.HttpContext.Response);
        }

        private async Task<IActionResult> ExchangeCoreAsync(string code, string codeVerifier, string clientId, string redirectUri, string tenantId, HttpRequest request, HttpResponse response)
        {
            var exchangeResult = await ExchangeToTokenSetAsync(code, codeVerifier, clientId, redirectUri, tenantId, request);
            if (exchangeResult.ErrorResult != null)
            {
                return exchangeResult.ErrorResult;
            }

            var clientRegistration = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            var useTokensCookie = clientRegistration?.UseTokensCookie ?? true;

            if (string.IsNullOrWhiteSpace(exchangeResult.AccessToken))
            {
                _logger.LogError("Access token generation failed for client {ClientId}", clientId);
                return new BadRequestObjectResult(new { error = "server_error", error_description = "Failed to generate access token" });
            }

            if (useTokensCookie && exchangeResult.CanSetCookies)
            {
                var cookieDomain = exchangeResult.CookieDomain;
                var tokenDomain = exchangeResult.Domain ?? exchangeResult.EffectiveTenantId;
                var cookiesSet = AppendAccessAndRefreshTokenCookies(
                    response,
                    tokenDomain,
                    exchangeResult.AccessToken,
                    exchangeResult.RefreshToken,
                    cookieDomain,
                    exchangeResult.AccessExpiry,
                    exchangeResult.RefreshExpiry);

                if (!cookiesSet)
                {
                    _logger.LogWarning("Failed to set authentication cookies for client {ClientId}, domain {TokenDomain}. Falling back to token response body.", clientId, tokenDomain);
                    return new OkObjectResult(new
                    {
                        access_token = exchangeResult.AccessToken,
                        id_token = exchangeResult.IdToken,
                        refresh_token = exchangeResult.RefreshToken,
                        token_type = "Bearer",
                        expires_in = exchangeResult.ExpiresIn,
                        scope = exchangeResult.Scope,
                        cookie_delivery_failed = true
                    });
                }

                return new OkObjectResult(new
                {
                    id_token = exchangeResult.IdToken,
                    token_type = "Bearer",
                    expires_in = exchangeResult.ExpiresIn,
                    scope = exchangeResult.Scope,
                    cookie_set = true
                });
            }

            if (useTokensCookie && !exchangeResult.CanSetCookies)
            {
                _logger.LogWarning("Cannot set cookies for client {ClientId}: domain resolution failed. Returning tokens in response body.", clientId);
            }

            return new OkObjectResult(new
            {
                access_token = exchangeResult.AccessToken,
                id_token = exchangeResult.IdToken,
                refresh_token = exchangeResult.RefreshToken,
                token_type = "Bearer",
                expires_in = exchangeResult.ExpiresIn,
                scope = exchangeResult.Scope,
                cookie_set = false
            });
        }

        private async Task<OidcExchangeResult> ExchangeToTokenSetAsync(string code, string codeVerifier, string clientId, string redirectUri, string tenantId, HttpRequest request)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(clientId))
            {
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_request", error_description = "Missing required parameters" }));
            }

            var resolvedTenantId = !string.IsNullOrWhiteSpace(tenantId)
                ? tenantId
                : request.HttpContext.User.FindFirst("tenant_id")?.Value;

            var (validation, authCode, user, effectiveTenantId) = await ValidateInputsAsync(code, codeVerifier, clientId, redirectUri, resolvedTenantId);
            if (validation != null)
            {
                return validation;
            }

            var tenant = _tenants.GetTenantByID(effectiveTenantId!);
            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(
                user!,
                authCode!.OrganizationId,
                authCode.Scope,
                requireExplicitScope: true);

            var tenantAudience = DomainResolver.GetAudience(tenant);

            var fullName = string.Join(' ', new[] { user!.FirstName, user.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            var claims = new OidcClaims
            {
                Sub = authCode.UserId,
                TenantId = effectiveTenantId!,
                OrgId = authCode.OrganizationId,
                Nonce = authCode.Nonce,
                AuthTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ClientId = clientId,
                Audience = tenantAudience,
                Scope = authCode.Scope,
                Email = user.Email,
                Name = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                UserName = user.UserName,
                Amr = authCode.Amr is { Count: > 0 } ? authCode.Amr : ["pwd"],
                Roles = resolvedClaims.Roles,
                Permissions = resolvedClaims.Permissions
            };


            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var accessTokenLifetimeSeconds = Math.Max((authConfiguration?.AccessTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes) * IdpConstants.SecondsPerMinute, IdpConstants.MinAccessTokenLifetimeSeconds);
            var absoluteRefreshTokenLifetimeMinutes = Math.Max(authConfiguration?.AbsoluteRefreshTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes, 1);

            var issuer = DomainResolver.GetIssuer(tenant);

            var idpSessionId = await ResolveOrCreateIdpSessionAsync(request, authCode.UserId, effectiveTenantId!);

            var idToken = await _tokenService.GenerateIdTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var accessToken = await _tokenService.GenerateAccessTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var refreshTokenModel = await _tokenService.GenerateRefreshTokenAsync(claims, issuer, false, idpSessionId);

            refreshTokenModel.UserId = authCode.UserId;
            refreshTokenModel.ClientId = clientId;
            refreshTokenModel.TenantId = effectiveTenantId!;
            refreshTokenModel.OrganizationId = authCode.OrganizationId;
            refreshTokenModel.Audience = tenantAudience;
            refreshTokenModel.Scope = authCode.Scope;
            refreshTokenModel.IpAddress = OidcRedirectUrlBuilder.GetClientIpAddress(request);
            refreshTokenModel.UserAgent = request.Headers["User-Agent"].ToString();
            refreshTokenModel.IssuedUtc = DateTime.UtcNow;
            await _refreshTokenRepo.CreateAsync(refreshTokenModel);

            _logger.LogInformation("Tokens issued for user {UserId}, client {ClientId}", authCode.UserId, clientId);

            var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, request);
            var accessExpiry = DateTime.UtcNow.AddSeconds(accessTokenLifetimeSeconds);
            var refreshExpiry = refreshTokenModel.AbsoluteExpiry == default
                ? DateTime.UtcNow.AddMinutes(absoluteRefreshTokenLifetimeMinutes)
                : refreshTokenModel.AbsoluteExpiry;

            return OidcExchangeResult.FromTokens(
                accessToken,
                idToken,
                refreshTokenModel.TokenId,
                effectiveTenantId!,
                isResolved ? domain : null,
                cookieDomain,
                authCode.Scope,
                accessTokenLifetimeSeconds,
                accessExpiry,
                refreshExpiry);
        }

        private async Task<(OidcExchangeResult? Error, AuthorizationCodeModel? AuthCode, User? User, string? EffectiveTenantId)> ValidateInputsAsync(
            string code,
            string codeVerifier,
            string clientId,
            string redirectUri,
            string? tenantId)
        {
            var authCode = await _authCodeRepo.GetByCodeAsync(code);
            if (authCode == null)
            {
                _logger.LogWarning("Authorization code not found: {Code}", code);
                return (OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code is invalid or expired" })), null, null, null);
            }

            if (!string.IsNullOrWhiteSpace(tenantId)
                && !string.IsNullOrWhiteSpace(authCode.TenantId)
                && !string.Equals(authCode.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Tenant mismatch for code exchange. Presented tenant: {TenantId}, code tenant: {CodeTenantId}", tenantId, authCode.TenantId);
                return (OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Tenant mismatch" })), null, null, null);
            }

            if (authCode.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Authorization code expired: {Code}", code);
                return (OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code has expired" })), null, null, null);
            }

            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            if (client == null || client.ClientId != authCode.ClientId)
            {
                _logger.LogWarning("Client validation failed for code exchange");
                return (OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_client" })), null, null, null);
            }

            if (string.IsNullOrWhiteSpace(clientId)
                || await _authenticationRepository.GetOidcClientRegistrationAsync(clientId) == null)
            {
                _logger.LogWarning("OIDC client config missing for code exchange: {ClientId}", clientId);
                return (OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_client", error_description = "Client configuration not found" })), null, null, null);
            }

            if (authCode.RedirectUri != redirectUri)
            {
                _logger.LogWarning("Redirect URI mismatch for code exchange");
                return (OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Redirect URI mismatch" })), null, null, null);
            }

            if (!string.IsNullOrWhiteSpace(codeVerifier))
            {
                var pkceValid = await _pkceService.ValidateVerifierAsync(authCode.CodeChallenge, codeVerifier, authCode.CodeChallengeMethod);
                if (!pkceValid)
                {
                    _logger.LogWarning("PKCE validation failed for client {ClientId}", clientId);
                    return (OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "PKCE code_verifier is invalid" })), null, null, null);
                }
            }

            var user = await _userRepository.GetUserByIdAsync(authCode.UserId);
            if (user == null)
            {
                return (OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "User not found" })), null, null, null);
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                _logger.LogWarning("Token exchange denied for locked account {UserId}", authCode.UserId);
                return (OidcExchangeResult.FromError(new ObjectResult(new { error = "account_locked", error_description = "Account is temporarily locked due to failed authentication attempts" })
                {
                    StatusCode = StatusCodes.Status423Locked
                }), null, null, null);
            }

            var effectiveTenantId = authCode.TenantId ?? tenantId ?? "default";
            return (null, authCode, user, effectiveTenantId);
        }

        private Task<string> ResolveOrCreateIdpSessionAsync(HttpRequest request, string userId, string tenantId)
        {
            // Thin wrapper: cookie resolution + account-add + cookie-write are all handled inside the helper.
            // Failure surfaces as an exception — caller maps the failure to invalid_grant.
            return _idpSessionService.ResolveOrCreateAsync(
                request.HttpContext,
                userId,
                tenantId,
                OidcRedirectUrlBuilder.GetClientIpAddress(request));
        }

        private static bool AppendAccessAndRefreshTokenCookies(
            HttpResponse response,
            string tokenDomain,
            string? accessToken,
            string? refreshToken,
            string? cookieDomain,
            DateTime accessExpiry,
            DateTime refreshExpiry)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false;
            }

            var isLocal = DomainResolver.IsLocalhost();
            var accessOptions = isLocal
                ? DomainResolver.CreateLoopbackCookieOptions(cookieDomain, accessExpiry)
                : DomainResolver.CreateProductionCookieOptions(cookieDomain, accessExpiry);
            var refreshOptions = isLocal
                ? DomainResolver.CreateLoopbackCookieOptions(cookieDomain, refreshExpiry)
                : DomainResolver.CreateProductionCookieOptions(cookieDomain, refreshExpiry);

            response.Cookies.Append($"{tokenDomain}", accessToken, accessOptions);

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                response.Cookies.Append($"{IdpConstants.RefreshTokenCookieName}_{tokenDomain}", refreshToken, refreshOptions);
            }

            return true;
        }

        internal sealed class OidcExchangeResult
        {
            public IActionResult? ErrorResult { get; private set; }
            public string AccessToken { get; private set; } = string.Empty;
            public string IdToken { get; private set; } = string.Empty;
            public string RefreshToken { get; private set; } = string.Empty;
            public string EffectiveTenantId { get; private set; } = string.Empty;
            public string? Domain { get; private set; }
            public string? CookieDomain { get; private set; }
            public bool CanSetCookies => !string.IsNullOrWhiteSpace(Domain);
            public string Scope { get; private set; } = string.Empty;
            public int ExpiresIn { get; private set; }
            public DateTime AccessExpiry { get; private set; }
            public DateTime RefreshExpiry { get; private set; }

            public static OidcExchangeResult FromError(IActionResult errorResult)
            {
                return new OidcExchangeResult { ErrorResult = errorResult };
            }

            public static OidcExchangeResult FromTokens(
                string accessToken,
                string idToken,
                string refreshToken,
                string effectiveTenantId,
                string? domain,
                string? cookieDomain,
                string scope,
                int expiresIn,
                DateTime accessExpiry,
                DateTime refreshExpiry)
            {
                return new OidcExchangeResult
                {
                    AccessToken = accessToken,
                    IdToken = idToken,
                    RefreshToken = refreshToken,
                    EffectiveTenantId = effectiveTenantId,
                    Domain = domain,
                    CookieDomain = cookieDomain,
                    Scope = scope,
                    ExpiresIn = expiresIn,
                    AccessExpiry = accessExpiry,
                    RefreshExpiry = refreshExpiry
                };
            }
        }
    }
}
