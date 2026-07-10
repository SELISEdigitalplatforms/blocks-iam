using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Services;
using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// Self-service session, history, idp-session and impersonation endpoints.
    /// Shares the <c>/iam</c> route prefix with <see cref="IamController"/> by design
    /// (plan decision: no <c>/security</c> segment, URLs stay under <c>/iam/*</c>).
    /// Action templates are disjoint from IamController, so route resolution is unambiguous.
    /// </summary>
    [ApiController]
    [Route("security")]
    [Authorize]
    public class SecurityController : ControllerBase
    {
        private readonly ISecurityQueryService _securityQueryService;
        private readonly ISessionRevocationService _sessionRevocationService;

        public SecurityController(
            ISecurityQueryService securityQueryService,
            ISessionRevocationService sessionRevocationService)
        {
            _securityQueryService = securityQueryService;
            _sessionRevocationService = sessionRevocationService;
        }

        [HttpGet("sessions")]
        public async Task<ActionResult<BaseQueryListResponse<IQueryable<SessionDto>>>> GetSessions([FromQuery] GetSessionsRequest req, CancellationToken ct)
        {
            var targetUserId = ResolveTargetUserId(req.UserId);
            var result = await _securityQueryService.GetSessionsAsync(targetUserId, req, ct);
            return Ok(result);
        }

        [HttpGet("sessions/{sessionId}")]
        public async Task<ActionResult<SessionDto?>> GetSession([FromRoute] string sessionId, CancellationToken ct)
        {
            var result = await _securityQueryService.GetSessionByIdAsync(sessionId, ct);
            if (result == null)
            {
                return NotFound(new { error = "session_not_found" });
            }
            return Ok(result);
        }

        [HttpGet("sessions/{sessionId}/refresh-tokens")]
        public async Task<ActionResult<IReadOnlyList<RefreshTokenRotationDto>>> GetSessionRefreshTokens([FromRoute] string sessionId, CancellationToken ct)
        {
            var session = await _securityQueryService.GetSessionByIdAsync(sessionId, ct);
            if (session == null)
            {
                return NotFound(new { error = "session_not_found" });
            }

            var actorUserId = ResolveActorUserId();
            if (!string.IsNullOrEmpty(actorUserId) && !string.IsNullOrEmpty(session.UserId) && actorUserId != session.UserId)
            {
                return Forbid();
            }

            var rows = await _securityQueryService.GetRotationHistoryAsync(sessionId, ct);
            return Ok(rows);
        }


        [HttpPost("sessions/{sessionId}/revoke")]
        public async Task<ActionResult<RevokeSessionResponse>> RevokeSession([FromRoute] string sessionId, [FromBody] RevokeSessionRequest? req, CancellationToken ct)
        {
            var session = await _securityQueryService.GetSessionByIdAsync(sessionId, ct);
            if (session == null)
            {
                return NotFound(new { error = "session_not_found" });
            }

            var actorUserId = ResolveActorUserId();
            var targetUserId = ResolveTargetUserId(session.UserId);

            var currentSessionId = await _securityQueryService.ResolveCurrentSessionIdAsync(actorUserId, ct);

            var result = await _sessionRevocationService.RevokeSessionAsync(sessionId, actorUserId, currentSessionId, targetUserId, req?.Reason, ct);

            if (result.Warnings.Contains("cannot_revoke_current_session"))
            {
                return BadRequest(new { error = "cannot_revoke_current_session", hint = "POST /auth/logout" });
            }

            if (req?.Reason != null || result.AlreadyRevoked || result.RevokedAt.HasValue)
            {
                if (!result.AlreadyRevoked && result.RevokedAt == null && result.RevokedRefreshTokens == 0 && result.Warnings.Count == 0)
                {
                    return NotFound(new { error = "session_not_found" });
                }
            }

            return Ok(result);
        }

        // [HttpGet("idp-sessions")]
        // public async Task<ActionResult<IdpSessionSummaryDto?>> GetIdpSession([FromQuery(Name = "userId")] string? userId, CancellationToken ct)
        // {
        //     var targetUserId = ResolveTargetUserId(userId);
        //     var result = await _securityQueryService.GetIdpSessionAsync(targetUserId, ct);
        //     if (result == null)
        //     {
        //         return NotFound(new { error = "idp_session_not_found" });
        //     }
        //     return Ok(result);
        // }

        // [HttpGet("impersonations")]
        // public async Task<ActionResult<List<ImpersonationSummaryDto>>> GetImpersonations([FromQuery(Name = "userId")] string? userId, CancellationToken ct)
        // {
        //     var targetUserId = ResolveTargetUserId(userId);
        //     var result = await _securityQueryService.GetImpersonationsAsync(targetUserId, ct);
        //     return Ok(result);
        // }

        private string ResolveActorUserId() => BlocksContext.GetContext()?.UserId ?? string.Empty;

        private string ResolveTargetUserId(string? requestedUserId)
        {
            if (!string.IsNullOrWhiteSpace(requestedUserId))
            {
                return requestedUserId;
            }
            return ResolveActorUserId();
        }
    }
}
