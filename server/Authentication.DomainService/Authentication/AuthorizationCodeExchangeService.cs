using Authentication.DomainService.Entities;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Iam.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using Blocks.Genesis.Entities.Iam;

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
        /// <summary>Revoke reason for the tokens of a code that was used a second time.</summary>
        public const string AuthorizationCodeReuseReason = "authorization_code_reuse";

        private readonly IAuthorizationCodeRepository _authCodeRepo;
        private readonly IRefreshTokenRepository _refreshTokenRepo;
        private readonly ITokenRevocationRepository _tokenRevocationRepo;
        private readonly ICacheClient _cacheClient;
        private readonly ITokenGenerationService _tokenService;
        private readonly IPkceService _pkceService;
        private readonly IUserRepository _userRepository;
        private readonly IAuthorizationClaimsResolver _authorizationClaimsResolver;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;
        private readonly ILogger<AuthorizationCodeExchangeService> _logger;
        private readonly IOidcClientTokenLifetimeService? _clientTokenLifetimes;

        public AuthorizationCodeExchangeService(
            IAuthorizationCodeRepository authCodeRepo,
            IRefreshTokenRepository refreshTokenRepo,
            ITokenGenerationService tokenService,
            IPkceService pkceService,
            IUserRepository userRepository,
            IAuthorizationClaimsResolver authorizationClaimsResolver,
            IAuthenticationRepository authenticationRepository,
            ITenants tenants,
            ITokenRevocationRepository tokenRevocationRepo,
            ICacheClient cacheClient,
            ILogger<AuthorizationCodeExchangeService> logger,
            IOidcClientTokenLifetimeService? clientTokenLifetimes = null)
        {
            _clientTokenLifetimes = clientTokenLifetimes;
            _authCodeRepo = authCodeRepo;
            _refreshTokenRepo = refreshTokenRepo;
            _tokenRevocationRepo = tokenRevocationRepo;
            _cacheClient = cacheClient;
            _tokenService = tokenService;
            _pkceService = pkceService;
            _userRepository = userRepository;
            _authorizationClaimsResolver = authorizationClaimsResolver;
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
            _logger = logger;
        }

        public async Task<IActionResult> ExchangeAsync(HttpRequest request)
        {
            var code = request.Form["code"].ToString();
            var codeVerifier = request.Form["code_verifier"].ToString();
            var redirectUri = request.Form["redirect_uri"].ToString();
            var (clientId, clientSecret) = OidcRedirectUrlBuilder.ReadClientCredentials(request);

            // Tenant ID resolution: form > query > header (X-Blocks-Key)
            var tenantId = !string.IsNullOrWhiteSpace(request.Form["tenant_id"].ToString())
                ? request.Form["tenant_id"].ToString()
                : (!string.IsNullOrWhiteSpace(request.Query["tenant_id"].ToString())
                    ? request.Query["tenant_id"].ToString()
                    : (request.Headers.TryGetValue("X-Blocks-Key", out var headerValue)
                        ? headerValue.ToString()
                        : string.Empty));

            return await ExchangeCoreAsync(new ExchangeInput(code, codeVerifier, clientId, clientSecret, redirectUri, tenantId), request, request.HttpContext.Response);
        }

        private sealed record ExchangeInput(string Code, string CodeVerifier, string ClientId, string ClientSecret, string RedirectUri, string TenantId);

        private async Task<IActionResult> ExchangeCoreAsync(ExchangeInput input, HttpRequest request, HttpResponse response)
        {
            var clientId = input.ClientId;
            var exchangeResult = await ExchangeToTokenSetAsync(input.Code, input.CodeVerifier, clientId, input.ClientSecret, input.RedirectUri, input.TenantId, request);
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

        private async Task<OidcExchangeResult> ExchangeToTokenSetAsync(string code, string codeVerifier, string clientId, string clientSecret, string redirectUri, string tenantId, HttpRequest request)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(clientId))
            {
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_request", error_description = "Missing required parameters" }));
            }

            var resolvedTenantId = !string.IsNullOrWhiteSpace(tenantId)
                ? tenantId
                : request.HttpContext.User.FindFirst("tenant_id")?.Value;

            var (validation, authCode, user, effectiveTenantId) = await ValidateInputsAsync(code, codeVerifier, clientId, clientSecret, redirectUri, resolvedTenantId);
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

            var roles = resolvedClaims.Roles;
            var permissions = resolvedClaims.Permissions;
            if (authCode.IsLinkAuthentication)
            {
                roles = IntersectClaims(resolvedClaims.Roles, authCode.RestrictedRoles);
                permissions = IntersectClaims(resolvedClaims.Permissions, authCode.RestrictedPermissions);
            }

            // Link sessions carry the tenant audience like every other code. Narrowing to the
            // link's client left the token rejected by every Blocks API, the construct's own
            // iam/me included -- see JwtAccessTokenProvider.MapJwtAccessToken.
            var tenantAudience = DomainResolver.GetAudience(tenant);
            var audience = tenantAudience;

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
                Audience = audience,
                Scope = authCode.Scope,
                Email = user.Email,
                Name = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                UserName = user.UserName,
                Amr = authCode.Amr is { Count: > 0 } ? authCode.Amr : ["pwd"],
                Roles = roles,
                Permissions = permissions,
                IsLinkAuthentication = authCode.IsLinkAuthentication,
                RestrictedRoles = authCode.RestrictedRoles?.ToList() ?? [],
                RestrictedPermissions = authCode.RestrictedPermissions?.ToList() ?? [],
                // The code is usually redeemed by a server; the refresh token records the browser
                // that signed in, as seen when this code was issued.
                SignInIpAddress = authCode.CreatedByIpAddress,
                SignInUserAgent = authCode.CreatedByUserAgent
            };


            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            // The client's own lifetimes, when set, replace the project values; otherwise the same instance is kept.
            if (_clientTokenLifetimes != null)
            {
                authConfiguration = (await _clientTokenLifetimes.ApplyAsync(authConfiguration, clientId))!;
            }
            var accessTokenLifetimeSeconds = Math.Max((authConfiguration?.AccessTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes) * IdpConstants.SecondsPerMinute, IdpConstants.MinAccessTokenLifetimeSeconds);
            var absoluteRefreshTokenLifetimeMinutes = Math.Max(authConfiguration?.AbsoluteRefreshTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultRememberMeRefreshTokenValidForNumberMinutes, 1);

            var issuer = DomainResolver.GetIssuer(tenant);

            if (string.IsNullOrWhiteSpace(authCode.IdpSessionId))
            {
                _logger.LogWarning("Authorization code is missing IdP session id");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code is missing IdP session" }));
            }

            var idpSessionId = authCode.IdpSessionId;
            claims.Sid = idpSessionId;

            var idToken = await _tokenService.GenerateIdTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var accessToken = await _tokenService.GenerateAccessTokenAsync(claims, issuer, accessTokenLifetimeSeconds);
            var refreshTokenModel = await _tokenService.GenerateRefreshTokenAsync(claims, issuer, false, idpSessionId);
            var accessExpiry = DateTime.UtcNow.AddSeconds(accessTokenLifetimeSeconds);

            // Record what this code minted before any cookie or answer leaves, so a reuse can revoke it.
            // If the code was reused (or is gone) meanwhile, these tokens are revoked and never handed out.
            var mint = new AuthorizationCodeMint
            {
                LineageId = refreshTokenModel.EffectiveRefreshTokenSessionId,
                Jti = ReadJti(accessToken),
                UserId = authCode.UserId,
                AccessExpiresAt = accessExpiry
            };
            if (!await _authCodeRepo.RecordMintAsync(authCode, mint))
            {
                _logger.LogWarning("Authorization code reuse refused for client {ClientId}", clientId);
                await _cacheClient.RemoveKeyAsync(refreshTokenModel.TokenId);
                await RevokeMintAsync(mint);
                return OidcExchangeResult.FromError(InvalidCode());
            }

            // UnifiedTokenSessionService.CreateOrRotateRefreshToken (called inside GenerateRefreshTokenAsync)
            // already persisted this refresh token to MongoDB with full SessionId/ClientId metadata.
            // Do NOT call _refreshTokenRepo.CreateAsync here — it would (a) duplicate by TokenId ([BsonId])
            // and (b) fail the tightened RefreshTokenRepository.CreateAsync validation when fields are empty.

            _logger.LogInformation("Tokens issued for user {UserId}, client {ClientId}", authCode.UserId, clientId);

            var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, request);
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
            string clientSecret,
            string redirectUri,
            string? tenantId)
        {
            var authCode = await _authCodeRepo.GetByCodeAsync(code);
            if (authCode == null)
            {
                _logger.LogWarning("Authorization code not found");
                return (OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code is invalid or expired" })), null, null, null);
            }

            var codeError = CheckCodeTenantAndExpiry(authCode, tenantId);
            if (codeError != null)
            {
                return (codeError, null, null, null);
            }

            var clientError = await CheckClientForCodeExchangeAsync(authCode, clientId, clientSecret);
            if (clientError != null)
            {
                return (clientError, null, null, null);
            }

            var proofError = await CheckRedirectUriAndPkceAsync(authCode, redirectUri, codeVerifier, clientId);
            if (proofError != null)
            {
                return (proofError, null, null, null);
            }

            var user = await _userRepository.GetUserByIdAsync(authCode.UserId);
            var userError = CheckCodeUser(user, authCode);
            if (userError != null)
            {
                return (userError, null, null, null);
            }

            var consumeError = await ConsumeAuthorizationCodeAsync(authCode, clientId);
            if (consumeError != null)
            {
                return (consumeError, null, null, null);
            }

            var effectiveTenantId = authCode.TenantId ?? tenantId ?? "default";
            return (null, authCode, user, effectiveTenantId);
        }

        private OidcExchangeResult? CheckCodeTenantAndExpiry(AuthorizationCodeModel authCode, string? tenantId)
        {
            if (!string.IsNullOrWhiteSpace(tenantId)
                && !string.IsNullOrWhiteSpace(authCode.TenantId)
                && !string.Equals(authCode.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Tenant mismatch for code exchange. Presented tenant: {TenantId}, code tenant: {CodeTenantId}", tenantId, authCode.TenantId);
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Tenant mismatch" }));
            }

            if (authCode.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Authorization code expired");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Authorization code has expired" }));
            }

            return null;
        }

        private async Task<OidcExchangeResult?> CheckClientForCodeExchangeAsync(AuthorizationCodeModel authCode, string clientId, string clientSecret)
        {
            var client = await _authenticationRepository.GetOidcClientRegistrationAsync(clientId);
            if (client == null || client.ClientId != authCode.ClientId)
            {
                _logger.LogWarning("Client validation failed for code exchange");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_client" }));
            }

            // A confidential client proves it is the client (RFC 6749 3.2.1); without this a leaked code
            // plus the public client_id and redirect_uri was enough. Same rule as the device flow.
            if (OidcClientValidator.RequiresClientAuthentication(client) && !OidcClientValidator.SecretsMatch(clientSecret, client.ClientSecret))
            {
                _logger.LogWarning("Client authentication failed for code exchange, client {ClientId}", clientId);
                return OidcExchangeResult.FromError(new UnauthorizedObjectResult(new { error = "invalid_client", error_description = "Client authentication failed" }));
            }

            // A public client has no secret, so PKCE is its only proof (RFC 9700 2.1.1).
            if (OidcClientValidator.IsPublicClient(client, GrantTypes.AuthCode) && string.IsNullOrWhiteSpace(authCode.CodeChallenge))
            {
                _logger.LogWarning("Public client code without PKCE refused, client {ClientId}", clientId);
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "PKCE is required for public clients" }));
            }

            if (string.IsNullOrWhiteSpace(clientId)
                || await _authenticationRepository.GetOidcClientRegistrationAsync(clientId) == null)
            {
                _logger.LogWarning("OIDC client config missing for code exchange: {ClientId}", clientId);
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_client", error_description = "Client configuration not found" }));
            }

            return null;
        }

        private async Task<OidcExchangeResult?> CheckRedirectUriAndPkceAsync(AuthorizationCodeModel authCode, string redirectUri, string codeVerifier, string clientId)
        {
            if (authCode.RedirectUri != redirectUri)
            {
                _logger.LogWarning("Redirect URI mismatch for code exchange");
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Redirect URI mismatch" }));
            }

            // A code issued with a PKCE challenge can only be redeemed with its verifier; skipping the
            // verifier must not skip the check (RFC 7636 4.6, RFC 9700 2.1.1).
            if (!string.IsNullOrWhiteSpace(authCode.CodeChallenge) && string.IsNullOrWhiteSpace(codeVerifier))
            {
                _logger.LogWarning("PKCE code_verifier missing for client {ClientId}", clientId);
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "PKCE code_verifier is required" }));
            }

            if (!string.IsNullOrWhiteSpace(codeVerifier))
            {
                var pkceValid = await _pkceService.ValidateVerifierAsync(authCode.CodeChallenge, codeVerifier, authCode.CodeChallengeMethod);
                if (!pkceValid)
                {
                    _logger.LogWarning("PKCE validation failed for client {ClientId}", clientId);
                    return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "PKCE code_verifier is invalid" }));
                }
            }

            return null;
        }

        private OidcExchangeResult? CheckCodeUser(User? user, AuthorizationCodeModel authCode)
        {
            if (user == null)
            {
                return OidcExchangeResult.FromError(new BadRequestObjectResult(new { error = "invalid_grant", error_description = "User not found" }));
            }

            if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
            {
                _logger.LogWarning("Token exchange denied for locked account {UserId}", authCode.UserId);
                return OidcExchangeResult.FromError(new ObjectResult(new { error = "account_locked", error_description = "Account is temporarily locked due to failed authentication attempts" })
                {
                    StatusCode = StatusCodes.Status423Locked
                });
            }

            return null;
        }

        private async Task<OidcExchangeResult?> ConsumeAuthorizationCodeAsync(AuthorizationCodeModel authCode, string clientId)
        {
            // Last check before tokens are minted, so a code is only spent by a request that passed
            // every other check. A second redemption of the same code is refused.
            var consumed = await _authCodeRepo.TryConsumeAsync(authCode);
            if (consumed == AuthorizationCodeConsumeResult.Consumed)
            {
                return null;
            }

            _logger.LogWarning("Authorization code reuse refused for client {ClientId}", clientId);
            if (consumed == AuthorizationCodeConsumeResult.Reused)
            {
                // Only the tokens minted from this code; other sessions of the user stay.
                var minted = await _authCodeRepo.MarkReusedAsync(authCode);
                if (minted != null)
                {
                    await RevokeMintAsync(minted);
                }
            }

            return OidcExchangeResult.FromError(InvalidCode());
        }

        private static BadRequestObjectResult InvalidCode() =>
            new(new { error = "invalid_grant", error_description = "Authorization code is invalid or expired" });

        /// <summary>
        /// Revokes the refresh-token lineage (MongoDB + Redis refresh cache) and the access token's jti.
        /// The access token itself still works at the APIs until it expires; only introspection sees it.
        /// </summary>
        private async Task RevokeMintAsync(AuthorizationCodeMint mint)
        {
            await _refreshTokenRepo.RevokeLineageAndEvictAsync(mint.LineageId, AuthorizationCodeReuseReason);
            if (!string.IsNullOrWhiteSpace(mint.Jti))
            {
                await _tokenRevocationRepo.RevokeTokenAsync(mint.Jti, mint.UserId, AuthorizationCodeReuseReason, mint.AccessExpiresAt);
            }
        }

        private static string ReadJti(string accessToken)
        {
            var handler = new JwtSecurityTokenHandler();
            return !string.IsNullOrWhiteSpace(accessToken) && handler.CanReadToken(accessToken)
                ? handler.ReadJwtToken(accessToken).Id
                : string.Empty;
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
