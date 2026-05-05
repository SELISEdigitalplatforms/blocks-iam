using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Blocks.Api.Controllers
{
    /// <summary>
    /// Token Revocation and Introspection Controller
    /// Implements RFC 7009 (Token Revocation) and RFC 7662 (Token Introspection)
    /// </summary>
    [ApiController]
    [Route("api/oidc")]
    public class TokenManagementController : ControllerBase
    {
        private readonly ITokenRevocationService _revocationService;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly ILogger<TokenManagementController> _logger;

        public TokenManagementController(
            ITokenRevocationService revocationService,
            IAuthenticationDomainService authenticationDomainService,
            ILogger<TokenManagementController> logger)
        {
            _revocationService = revocationService;
            _authenticationDomainService = authenticationDomainService;
            _logger = logger;
        }

        /// <summary>
        /// RFC 7009: Token Revocation Endpoint
        /// Allows clients and resource owners to revoke access and refresh tokens
        /// </summary>
        [HttpPost("revoke")]
        [Authorize]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> RevokeToken(
            [FromForm] string token,
            [FromForm] string token_type_hint,
            [FromForm] string client_id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return BadRequest(new { error = "invalid_request", error_description = "token parameter is required" });
                }

                var result = await _revocationService.RevokeTokenAsync(token, token_type_hint, client_id);

                if (!result.Success)
                {
                    _logger.LogWarning($"Token revocation failed: {result.Error}");
                    return BadRequest(new { error = result.Error, error_description = result.ErrorDescription });
                }

                _logger.LogInformation($"Token revoked successfully, hint: {token_type_hint}");

                var userId = User.FindFirst("sub")?.Value ?? string.Empty;
                await PublishTimelineAsync(
                    userId,
                    $"oidc_revoke_{(string.IsNullOrWhiteSpace(token_type_hint) ? "token" : token_type_hint)}",
                    "call_api_to_oidc_revoke");

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
        [Authorize]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> IntrospectToken(
            [FromForm] string token,
            [FromForm] string token_type_hint,
            [FromForm] string client_id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return BadRequest(new { error = "invalid_request", error_description = "token parameter is required" });
                }

                var result = await _revocationService.IntrospectTokenAsync(token, token_type_hint, client_id);

                _logger.LogInformation($"Token introspection: active={result.Active}, client={client_id}");

                var actorId = User.FindFirst("sub")?.Value ?? client_id;
                await PublishTimelineAsync(
                    actorId,
                    result.Active ? "oidc_introspect_active_token" : "oidc_introspect_inactive_token",
                    "call_api_to_oidc_introspect");

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

        /// <summary>
        /// Revoke all tokens for authenticated user (logout endpoint)
        /// Closes all sessions across all devices
        /// </summary>
        [HttpPost("logout-all")]
        [Authorize]
        public async Task<IActionResult> RevokeAllUserTokens()
        {
            try
            {
                var userId = User.FindFirst("sub")?.Value;
                var tenantId = User.FindFirst("tenant_id")?.Value;

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized(new { error = "invalid_request", error_description = "User not authenticated" });
                }

                var success = await _revocationService.RevokeAllUserTokensAsync(userId, tenantId ?? string.Empty, "logout_all");

                if (!success)
                {
                    return StatusCode(500, new { error = "server_error", error_description = "Failed to revoke tokens" });
                }

                _logger.LogInformation($"All tokens revoked for user {userId}");

                await PublishTimelineAsync(userId, "oidc_revoke_access_by_logout_all", "call_api_to_oidc_logout_all");

                return Ok(new { message = "All tokens revoked successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking all user tokens");
                return StatusCode(500, new { error = "server_error" });
            }
        }

        /// <summary>
        /// Get revocation history for audit trail
        /// </summary>
        [HttpGet("revocation-history")]
        [Authorize]
        public async Task<IActionResult> GetRevocationHistory()
        {
            try
            {
                var userId = User.FindFirst("sub")?.Value;

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized(new { error = "invalid_request", error_description = "User not authenticated" });
                }

                var history = await _revocationService.GetRevocationHistoryAsync(userId);

                return Ok(new
                {
                    user_id = userId,
                    revocation_count = history?.Count() ?? 0,
                    revocations = history?.Select(r => new
                    {
                        jti = r.Jti,
                        revoked_at = r.RevokedAt,
                        reason = r.RevokeReason,
                        expires_at = r.ExpiresAt
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving revocation history");
                return StatusCode(500, new { error = "server_error" });
            }
        }

        private async Task PublishTimelineAsync(string userId, string eventName, string actionBy)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var timelineEvent = new UserAuthenticationTimelineEvent
            {
                UserId = userId,
                Event = eventName,
                ActionBy = actionBy,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(Request?.Headers?.UserAgent.ToString() ?? string.Empty),
                IpAddresses = string.Join(",", _authenticationDomainService.GetVisitorsIpAddresses(Request.HttpContext))
            };

            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, timelineEvent);
        }
    }
}

