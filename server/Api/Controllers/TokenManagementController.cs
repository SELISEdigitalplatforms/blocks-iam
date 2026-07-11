using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Blocks.Api.Controllers
{
    /// <summary>
    /// Token Revocation and Introspection Controller
    /// Implements RFC 7009 (Token Revocation) and RFC 7662 (Token Introspection)
    /// </summary>
    [ApiController]
    [Route("oidc")]
    public class TokenManagementController : ControllerBase
    {
        private readonly ITokenRevocationService _revocationService;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly ILogger<TokenManagementController> _logger;

        public TokenManagementController(
            ITokenRevocationService revocationService,
            IAuthenticationRepository authenticationRepository,
            IAuthenticationDomainService authenticationDomainService,
            IUserActivityDispatcher userActivityDispatcher,
            ILogger<TokenManagementController> logger)
        {
            _revocationService = revocationService;
            _authenticationRepository = authenticationRepository;
            _authenticationDomainService = authenticationDomainService;
            _userActivityDispatcher = userActivityDispatcher;
            _logger = logger;
        }

        /// <summary>
        /// RFC 7009: Token Revocation Endpoint
        /// Allows clients and resource owners to revoke access and refresh tokens
        /// </summary>
        [HttpPost("revoke")]
        [AllowAnonymous]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> RevokeToken(
            [FromForm] string token,
            [FromForm] string? token_type_hint,
            [FromForm] string? client_id,
            [FromForm] string? client_secret)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return BadRequest(new { error = "invalid_request", error_description = "token parameter is required" });
                }

                var authenticatedClientId = await AuthenticateClientAsync(client_id, client_secret);
                if (string.IsNullOrWhiteSpace(authenticatedClientId))
                {
                    return Unauthorized(new { error = "invalid_client", error_description = "Client authentication failed" });
                }

                var result = await _revocationService.RevokeTokenAsync(token, token_type_hint ?? string.Empty, authenticatedClientId);

                if (!result.Success)
                {
                    _logger.LogWarning($"Token revocation failed: {result.Error}");

                    if (string.Equals(result.Error, "invalid_client", StringComparison.OrdinalIgnoreCase))
                    {
                        return Unauthorized(new { error = result.Error, error_description = result.ErrorDescription });
                    }

                    return BadRequest(new { error = result.Error, error_description = result.ErrorDescription });
                }

                _logger.LogInformation($"Token revoked successfully, hint: {token_type_hint}");

                var userId = User.FindFirst("sub")?.Value ?? string.Empty;
                await PublishTimelineAsync(
                    userId,
                    $"oidc_revoke_{(string.IsNullOrWhiteSpace(token_type_hint) ? "token" : token_type_hint)}",
                    "call_api_to_oidc_revoke",
                    "success",
                    null,
                    authenticatedClientId,
                    token_type_hint);

                // RFC 7009: Always return 200 OK on revocation request
                // Even if token doesn't exist or is invalid
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in token revocation endpoint");
                return StatusCode(500, new { error = "server_error" });
            }
        }

        /// <summary>
        /// RFC 7662: Token Introspection Endpoint
        /// Allows authorized clients to introspect tokens and get claims/metadata
        /// </summary>
        [HttpPost("introspect")]
        [AllowAnonymous]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> IntrospectToken(
            [FromForm] string token,
            [FromForm] string? token_type_hint,
            [FromForm] string? client_id,
            [FromForm] string? client_secret)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return BadRequest(new { error = "invalid_request", error_description = "token parameter is required" });
                }

                var authenticatedClientId = await AuthenticateClientAsync(client_id, client_secret);
                if (string.IsNullOrWhiteSpace(authenticatedClientId))
                {
                    return Unauthorized(new { error = "invalid_client", error_description = "Client authentication failed" });
                }

                var result = await _revocationService.IntrospectTokenAsync(token, token_type_hint ?? string.Empty, authenticatedClientId);

                _logger.LogInformation($"Token introspection: active={result.Active}, client={authenticatedClientId}");

                var actorId = User.FindFirst("sub")?.Value ?? authenticatedClientId;
                await PublishTimelineAsync(
                    actorId,
                    result.Active ? "oidc_introspect_active_token" : "oidc_introspect_inactive_token",
                    "call_api_to_oidc_introspect",
                    result.Active ? "success" : "failed",
                    result.Active ? null : "inactive_or_unauthorized",
                    authenticatedClientId,
                    token_type_hint);

                // RFC 7662: Return token metadata
                return Ok(new
                {
                    active = result.Active,
                    scope = result.Scope,
                    client_id = result.ClientId,
                    username = result.Username,
                    token_type = result.TokenType,
                    exp = result.Exp,
                    iat = result.Iat,
                    nbf = result.Nbf,
                    sub = result.Sub,
                    iss = result.Iss,
                    aud = result.Aud,
                    jti = result.Jti
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in token introspection endpoint");
                return StatusCode(500, new { error = "server_error" });
            }
        }

        private async Task<string?> AuthenticateClientAsync(string? clientIdFromForm, string? clientSecretFromForm)
        {
            string? basicClientId = null;
            string? basicClientSecret = null;

            var authHeader = Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                var encoded = authHeader.Substring("Basic ".Length).Trim();
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    var separatorIndex = decoded.IndexOf(':');
                    if (separatorIndex > 0)
                    {
                        basicClientId = decoded.Substring(0, separatorIndex);
                        basicClientSecret = decoded[(separatorIndex + 1)..];
                    }
                }
                catch
                {
                    return null;
                }
            }

            var resolvedClientId = basicClientId ?? clientIdFromForm;
            var resolvedClientSecret = basicClientSecret ?? clientSecretFromForm;

            if (string.IsNullOrWhiteSpace(resolvedClientId) || string.IsNullOrWhiteSpace(resolvedClientSecret))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(basicClientId)
                && !string.IsNullOrWhiteSpace(clientIdFromForm)
                && !string.Equals(basicClientId, clientIdFromForm, StringComparison.Ordinal))
            {
                return null;
            }

            var registration = await _authenticationRepository.GetOidcClientRegistrationAsync(resolvedClientId);
            if (registration == null || !registration.IsActive || string.IsNullOrWhiteSpace(registration.ClientSecret))
            {
                return null;
            }

            if (!string.Equals(registration.ClientSecret, resolvedClientSecret, StringComparison.Ordinal))
            {
                return null;
            }

            return resolvedClientId;
        }

        private async Task PublishTimelineAsync(string userId, string eventName, string actionBy, string outcome, string? reasonCode, string? clientId, string? tokenTypeHint)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
            {
                UserId = userId,
                Category = UserActivityCategory.Auth,
                Event = eventName,
                Source = "auth-token-management",
                SessionId = Request.Cookies[IdpConstants.BuildIdpSessionCookieKey(User.FindFirst("tenant_id")?.Value)],
                ClientId = clientId,
                CorrelationId = HttpContext.TraceIdentifier,
                Outcome = outcome,
                ReasonCode = reasonCode,
                TenantId = User.FindFirst("tenant_id")?.Value,
                Context = new ActivityContext
                {
                    IpAddress = string.Join(",", _authenticationDomainService.GetVisitorsIpAddresses(Request.HttpContext)),
                    DeviceInformation = _authenticationDomainService.GetDeviceInfo(Request?.Headers?.UserAgent.ToString() ?? string.Empty)
                },
                Metadata = new Dictionary<string, string>
                {
                    { "actionBy", actionBy },
                    { "tokenTypeHint", tokenTypeHint ?? string.Empty }
                }
            });
        }
    }
}

