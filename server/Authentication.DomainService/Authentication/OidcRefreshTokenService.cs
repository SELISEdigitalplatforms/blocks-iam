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
using Authentication.DomainService.Shared.Services;
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
        /// <summary>The only impersonation-session status a refresh may continue.</summary>
        private const string ActiveImpersonationStatus = "active";

        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IRefreshSessionResolver _refreshSessionResolver;
        private readonly ILogger<OidcRefreshTokenService> _logger;

        public OidcRefreshTokenService(
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            ITenants tenants,
            RefreshTokenAuthenticationService refreshTokenAuthenticationService,
            IAuthenticationService authenticationService,
            IRefreshTokenRepository refreshTokenRepository,
            IRefreshSessionResolver refreshSessionResolver,
            ILogger<OidcRefreshTokenService> logger)
        {
            _authenticationRepository = authenticationRepository;
            _cacheClient = cacheClient;
            _tenants = tenants;
            _refreshTokenAuthenticationService = refreshTokenAuthenticationService;
            _authenticationService = authenticationService;
            _refreshTokenRepository = refreshTokenRepository;
            _refreshSessionResolver = refreshSessionResolver;
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

            // A resolved token id that differs from the presented one is a grace-window replay: the
            // successor is returned as-is, without consuming another rotation.
            var graceReplayTokenId = string.Equals(tokenCache!.RefreshToken, refreshToken, StringComparison.Ordinal)
                ? null
                : tokenCache.RefreshToken;

            // Impersonation is restored BEFORE the rotation, never after it.
            //
            // This used to be two rotations: the lineage was first rotated into a plain root token pair,
            // and ExecuteImpersonateAsync then rotated that into an impersonated one. The intermediate
            // was never sent to the browser, but it was persisted unrevoked AND the impersonated
            // predecessor was revoked pointing straight at it, so RefreshSessionResolver's grace-window
            // chain walk could resolve a concurrent refresh onto a root token -- and did so permanently
            // whenever the second rotation failed. A project screen then held root-scoped cookies behind
            // an HTTP 200 that nothing downstream could tell apart from success.
            //
            // Resolving first collapses it to one impersonated -> impersonated rotation. No root token is
            // ever written, so there is nothing root for the chain to reach.
            var (impersonationError, impersonationSession) = await ResolveImpersonationForRefreshAsync(tokenCache);
            if (impersonationError != null)
            {
                return impersonationError;
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.RefreshToken,
                // A HINT only, no longer a replay: JwtAccessTokenProvider re-resolves the scope
                // against the user's current membership, so a revoked organization cannot survive
                // a refresh. The blank -> "default" collapse that used to live here handed the
                // tenant-wide scope to any cache entry that had lost its organization.
                OrganizationId = tokenCache.OrganizationId ?? string.Empty,
                ClientId = tokenCache.ClientId,
                RefreshToken = refreshToken,
                Request = request,
                // Carry the lineage's IdP session forward. Without this the mint fell back to
                // resolving from the request cookie, so a client refreshing without that cookie
                // (mobile, native, server-side) started a brand-new session on every refresh.
                IdpSessionId = tokenCache.SessionId,
                GraceReplayTokenId = graceReplayTokenId,
                GraceReplayAbsoluteExpiry = graceReplayTokenId == null ? null : tokenCache.AbsoluteExpiresUtc,
                // The single rotation carries the impersonation forward itself: JwtAccessTokenProvider
                // mints the target-tenant claims and CreateOrRotateRefreshToken persists the successor
                // with Impersonated = true, so the successor is a continuation rather than a de-escalation.
                IsImpersonation = impersonationSession != null,
                OriginalTenantId = impersonationSession == null ? null : tokenCache.TenantId,
                TargetTenantId = impersonationSession?.TargetTenantId,
                ImpersonationSessionId = impersonationSession == null ? null : tokenCache.ImpersonationId
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

            if (impersonationSession != null)
            {
                await TouchImpersonationSessionAsync(tokenCache.ImpersonationId!, tokenCache.OrganizationId);
            }

            return BuildRefreshTokenResponse(client, response, request);
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

            // One shared check decides validity for every refresh consumer: unrevoked, inside the sliding
            // window and inside the absolute cap, with a rotation superseded moments ago resolving to its
            // successor instead of failing.
            var tokenCache = await _refreshSessionResolver.TryResolveRefreshSessionAsync(refreshToken, configuration);

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

        /// <summary>
        /// Decides whether this refresh continues an impersonated session, and refuses the refresh when
        /// it cannot.
        /// </summary>
        /// <remarks>
        /// Every rejection here happens before the rotation, so the presented impersonated token is left
        /// intact and no token of any kind is written: the caller retries or logs out. Failing open in any
        /// one of these branches is what put root-scoped cookies on a project screen, so each of them
        /// ends the refresh rather than continuing without impersonation.
        /// </remarks>
        private async Task<(IActionResult? Error, ImpersonationSession? Session)> ResolveImpersonationForRefreshAsync(RefreshTokenCache tokenCache)
        {
            if (!tokenCache.Impersonated)
            {
                return (null, null);
            }

            if (string.IsNullOrWhiteSpace(tokenCache.ImpersonationId))
            {
                // Flagged impersonated but naming no session, so the target tenant is unknowable.
                // Continuing would mint a root token for a browser that believes it is inside a project.
                _logger.LogWarning(
                    "Impersonated refresh token names no impersonation session. reason=impersonation_id_missing userId={UserId} tenantId={TenantId}",
                    tokenCache.UserId,
                    tokenCache.TenantId);

                return (InvalidGrant("Impersonated session cannot be restored"), null);
            }

            ImpersonationSession? session;
            try
            {
                session = await _authenticationRepository.GetImpersonationSessionByIdAsync(tokenCache.ImpersonationId);
            }
            catch (Exception ex)
            {
                // A store outage is not a licence to de-impersonate. Nothing is written and the caller
                // can retry against the token it still holds.
                _logger.LogError(
                    ex,
                    "Impersonation session lookup failed during refresh. reason=session_lookup_failed impersonationId={ImpersonationId} userId={UserId}",
                    tokenCache.ImpersonationId,
                    tokenCache.UserId);

                return (new ObjectResult(new
                {
                    error = "temporarily_unavailable",
                    error_description = "Impersonated session could not be verified"
                })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                }, null);
            }

            if (session == null || !string.Equals(session.Status, ActiveImpersonationStatus, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Impersonation session is not active during refresh. reason=session_not_active impersonationId={ImpersonationId} status={Status} userId={UserId}",
                    tokenCache.ImpersonationId,
                    session?.Status ?? "missing",
                    tokenCache.UserId);

                return (InvalidGrant("Impersonation session is no longer active"), null);
            }

            if (string.IsNullOrWhiteSpace(session.TargetTenantId) || _tenants.GetTenantByID(session.TargetTenantId) == null)
            {
                // A tenant-cache miss used to surface as a failed second rotation, which is precisely the
                // interruption that stranded a root token at the head of the lineage.
                _logger.LogWarning(
                    "Impersonation refresh denied: target tenant does not resolve. reason=target_tenant_unresolvable impersonationId={ImpersonationId} targetTenantId={TargetTenantId}",
                    tokenCache.ImpersonationId,
                    session.TargetTenantId);

                return (new BadRequestObjectResult(new
                {
                    error = "invalid_target_tenant",
                    error_description = "Target tenant does not exist"
                }), null);
            }

            // Access is re-checked on every refresh, exactly as the ExecuteImpersonateAsync detour did.
            // Dropping it would let a revoked project share keep refreshing until the absolute cap. The
            // check fails closed on its own errors, so an unverifiable share ends the session instead of
            // silently widening it.
            if (!await _authenticationService.IsTenantSharedWithUserAsync(tokenCache.UserId!, session.TargetTenantId))
            {
                _logger.LogWarning(
                    "Impersonation refresh denied: target tenant is not shared with the user. reason=not_shared_with_user userId={UserId} targetTenantId={TargetTenantId}",
                    tokenCache.UserId,
                    session.TargetTenantId);

                return (new ObjectResult(new
                {
                    error = "forbidden",
                    error_description = "Target tenant is not shared with the requesting user"
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                }, null);
            }

            return (null, session);
        }

        private static BadRequestObjectResult InvalidGrant(string description) =>
            new(new { error = "invalid_grant", error_description = description });

        /// <summary>
        /// Keeps the session's activity stamp and organization current, which the switch-organization
        /// detour used to do as a side effect of every impersonated refresh. Best effort by design: the
        /// tokens are already issued and already in the response, so a bookkeeping failure must not turn a
        /// completed refresh into an error the caller would react to by logging out.
        /// </summary>
        private async Task TouchImpersonationSessionAsync(string impersonationSessionId, string? organizationId)
        {
            try
            {
                await _authenticationRepository.UpdateImpersonationSessionAsync(
                    impersonationSessionId,
                    new Dictionary<string, object>
                    {
                        { "OrganizationId", string.IsNullOrWhiteSpace(organizationId) ? "default" : organizationId },
                        { "LastActivity", DateTime.UtcNow }
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Impersonation session activity stamp could not be updated after a refresh. reason=session_touch_failed impersonationId={ImpersonationId}",
                    impersonationSessionId);
            }
        }

        private IActionResult BuildRefreshTokenResponse(
            OidcClientRegistration client,
            TokenResponse response,
            HttpRequest request)
        {
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
