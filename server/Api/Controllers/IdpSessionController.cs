using Authentication.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Idp.DomainService.Oidc.Contracts;

namespace Blocks.Api.Controllers
{
    /// <summary>
    /// IdP Session Controller
    /// Endpoints for OIDC multi-account SSO (single sign-on) session management
    /// Phase 5: IdP Session & Multi-Account SSO
    /// </summary>
    [ApiController]
    [Route("api/oidc/session")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class IdpSessionController : ControllerBase
    {
        private const string IdpSessionCookieName = "idp_session_id";

        private readonly IIdpSessionService _sessionService;
        private readonly ILogger<IdpSessionController> _logger;

        public IdpSessionController(
            IIdpSessionService sessionService,
            ILogger<IdpSessionController> logger)
        {
            _sessionService = sessionService;
            _logger = logger;
        }

        /// <summary>
        /// Get current session details
        /// GET /api/oidc/session
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(GetSessionResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetSession()
        {
            try
            {
                var sessionId = GetSessionIdFromToken();
                if (string.IsNullOrEmpty(sessionId))
                {
                    _logger.LogWarning("Session ID not found in token");
                    return Unauthorized(new { error = "session_not_found" });
                }

                var session = await _sessionService.GetSessionAsync(sessionId);
                if (session == null || session.RevokedAt.HasValue)
                {
                    _logger.LogWarning($"Session not found or revoked: {sessionId}");
                    return NotFound(new { error = "session_not_found" });
                }

                if (session.IsExpired())
                {
                    _logger.LogWarning($"Session expired: {sessionId}");
                    return Unauthorized(new { error = "session_expired" });
                }

                var response = new GetSessionResponse
                {
                    SessionId = session.SessionId,
                    Accounts = session.Accounts.Select(a => new AccountInfo
                    {
                        UserId = a.UserId,
                        TenantId = a.TenantId,
                        DisplayName = a.DisplayName ?? a.UserId,
                        LoginAt = a.LoginAt
                    }).ToList(),
                    CreatedAt = session.CreatedAt,
                    LastActivityAt = session.LastActivityAt,
                    IdleExpiresAt = session.IdleExpiry,
                    AbsoluteExpiresAt = session.AbsoluteExpiry
                };

                await _sessionService.UpdateActivityAsync(sessionId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session");
                return StatusCode(500, new { error = "internal_server_error" });
            }
        }

        /// <summary>
        /// Get all accounts in current session
        /// GET /api/oidc/session/accounts
        /// Supports multi-account SSO - user can see all accounts logged in this session
        /// </summary>
        [HttpGet("accounts")]
        [ProducesResponseType(typeof(GetAccountsResponse), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetAccounts()
        {
            try
            {
                var sessionId = GetSessionIdFromToken();
                if (string.IsNullOrEmpty(sessionId))
                {
                    return Unauthorized(new { error = "session_not_found" });
                }

                var isActive = await _sessionService.IsSessionActiveAsync(sessionId);
                if (!isActive)
                {
                    return Unauthorized(new { error = "session_inactive" });
                }

                var accounts = await _sessionService.GetAccountsAsync(sessionId);
                var response = new GetAccountsResponse
                {
                    Accounts = accounts.Select(a => new AccountInfo
                    {
                        UserId = a.UserId,
                        TenantId = a.TenantId,
                        DisplayName = a.DisplayName ?? a.UserId,
                        LoginAt = a.LoginAt
                    }).ToList()
                };

                await _sessionService.UpdateActivityAsync(sessionId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting accounts");
                return StatusCode(500, new { error = "internal_server_error" });
            }
        }

        /// <summary>
        /// Add another account to current session
        /// POST /api/oidc/session/add-account
        /// Allows user to authenticate with another account and add to same session
        /// OIDC multi-account SSO support
        /// </summary>
        [HttpPost("add-account")]
        [ProducesResponseType(typeof(AddAccountResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> AddAccount([FromBody] AddAccountRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.TenantId))
                {
                    return BadRequest(new { error = "invalid_request", error_description = "UserId and TenantId required" });
                }

                var sessionId = GetSessionIdFromToken();
                if (string.IsNullOrEmpty(sessionId))
                {
                    return Unauthorized(new { error = "session_not_found" });
                }

                var success = await _sessionService.AddAccountAsync(
                    sessionId,
                    request.UserId,
                    request.TenantId,
                    request.DisplayName);

                if (!success)
                {
                    return BadRequest(new { error = "failed_to_add_account" });
                }

                await _sessionService.UpdateActivityAsync(sessionId);
                return Ok(new AddAccountResponse { Success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding account");
                return StatusCode(500, new { error = "internal_server_error" });
            }
        }

        /// <summary>
        /// Select which account to use in this session
        /// POST /api/oidc/session/select-account
        /// For multi-account SSO, switches context to different account
        /// CSRF protection required
        /// </summary>
        [HttpPost("select-account")]
        [ProducesResponseType(typeof(SelectAccountResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> SelectAccount([FromBody] SelectAccountRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.UserId))
                {
                    return BadRequest(new { error = "invalid_request", error_description = "UserId required" });
                }

                var sessionId = GetSessionIdFromToken();
                if (string.IsNullOrEmpty(sessionId))
                {
                    return Unauthorized(new { error = "session_not_found" });
                }

                var success = await _sessionService.SelectAccountAsync(sessionId, request.UserId);
                if (!success)
                {
                    return BadRequest(new { error = "account_not_found_in_session" });
                }

                await _sessionService.UpdateActivityAsync(sessionId);
                return Ok(new SelectAccountResponse { Success = true, UserId = request.UserId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error selecting account");
                return StatusCode(500, new { error = "internal_server_error" });
            }
        }

        /// <summary>
        /// Remove account from session
        /// DELETE /api/oidc/session/accounts/{userId}
        /// Removes account from multi-account session
        /// </summary>
        [HttpDelete("accounts/{userId}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> RemoveAccount(string userId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return BadRequest(new { error = "invalid_request", error_description = "UserId required" });
                }

                var sessionId = GetSessionIdFromToken();
                if (string.IsNullOrEmpty(sessionId))
                {
                    return Unauthorized(new { error = "session_not_found" });
                }

                var success = await _sessionService.RemoveAccountAsync(sessionId, userId);
                if (!success)
                {
                    return BadRequest(new { error = "failed_to_remove_account" });
                }

                await _sessionService.UpdateActivityAsync(sessionId);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing account");
                return StatusCode(500, new { error = "internal_server_error" });
            }
        }

        /// <summary>
        /// Revoke current session (logout)
        /// POST /api/oidc/session/revoke
        /// Logs out all accounts in this session
        /// CSRF protection required
        /// </summary>
        [HttpPost("revoke")]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> RevokeSession([FromBody] RevokeSessionRequest request = null)
        {
            try
            {
                var sessionId = GetSessionIdFromToken();
                if (string.IsNullOrEmpty(sessionId))
                {
                    return Unauthorized(new { error = "session_not_found" });
                }

                var reason = request?.Reason ?? "user_logout";
                var success = await _sessionService.RevokeSessionAsync(sessionId, reason);

                if (!success)
                {
                    return BadRequest(new { error = "failed_to_revoke_session" });
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking session");
                return StatusCode(500, new { error = "internal_server_error" });
            }
        }

        // Helper method
        private string GetSessionIdFromToken()
        {
            // Prefer sid claim, fallback to browser IdP session cookie.
            var sessionIdClaim = User?.FindFirst("sid")?.Value;
            return string.IsNullOrWhiteSpace(sessionIdClaim)
                ? Request.Cookies[IdpSessionCookieName]
                : sessionIdClaim;
        }
    }

    // Request/Response Models
    public class GetSessionResponse
    {
        public string SessionId { get; set; }
        public List<AccountInfo> Accounts { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public DateTime IdleExpiresAt { get; set; }
        public DateTime AbsoluteExpiresAt { get; set; }
    }

    public class GetAccountsResponse
    {
        public List<AccountInfo> Accounts { get; set; }
    }

    public class AccountInfo
    {
        public string UserId { get; set; }
        public string TenantId { get; set; }
        public string DisplayName { get; set; }
        public DateTime LoginAt { get; set; }
    }

    public class AddAccountRequest
    {
        public string UserId { get; set; }
        public string TenantId { get; set; }
        public string DisplayName { get; set; }
    }

    public class AddAccountResponse
    {
        public bool Success { get; set; }
    }

    public class SelectAccountRequest
    {
        public string UserId { get; set; }
    }

    public class SelectAccountResponse
    {
        public bool Success { get; set; }
        public string UserId { get; set; }
    }

    public class RevokeSessionRequest
    {
        public string Reason { get; set; }
    }
}

