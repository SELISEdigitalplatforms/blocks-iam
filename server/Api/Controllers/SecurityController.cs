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
            var result = await _securityQueryService.GetSessionsAsync(req, ct);
            return Ok(result);
        }

        [HttpGet("sessions/{sessionId}")]
        public async Task<ActionResult<SessionDto?>> GetSession([FromRoute] string sessionId, CancellationToken ct)
        {
            var result = await _securityQueryService.GetSessionAsync(sessionId, ct);
            if (result == null)
            {
                return NotFound(new { error = "session_not_found" });
            }
            return Ok(result);
        }

        [HttpGet("sessions/{sessionId}/timeline")]
        public async Task<ActionResult<SessionTimelineDto?>> GetSessionTimeline([FromRoute] string sessionId, CancellationToken ct)
        {
            var result = await _securityQueryService.GetSessionTimelineAsync(sessionId, ct);
            if (result == null)
            {
                return NotFound(new { error = "session_not_found" });
            }
            return Ok(result);
        }

        [HttpPost("sessions/{sessionId}/revoke")]
        public async Task<ActionResult<RevokeSessionResponse>> RevokeSession([FromRoute] string sessionId, [FromBody] RevokeSessionRequest? req, CancellationToken ct)
        {
            var bc = BlocksContext.GetContext();
            var actorUserId = bc?.UserId;
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return Unauthorized();
            }

            var currentSessionId = await _securityQueryService.ResolveCurrentSessionIdAsync(actorUserId!, ct);

            var result = await _sessionRevocationService.RevokeSessionAsync(sessionId, actorUserId!, currentSessionId, req?.Reason, ct);

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

        [HttpGet("history")]
        public async Task<ActionResult<BaseQueryListResponse<IQueryable<AuthHistoryDto>>>> GetHistory([FromQuery] GetHistoryRequest req, CancellationToken ct)
        {
            var result = await _securityQueryService.GetHistoryAsync(req, ct);
            return Ok(result);
        }

        [HttpGet("idp-sessions")]
        public async Task<ActionResult<IdpSessionSummaryDto?>> GetIdpSession(CancellationToken ct)
        {
            var result = await _securityQueryService.GetIdpSessionAsync(ct);
            if (result == null)
            {
                return NotFound(new { error = "idp_session_not_found" });
            }
            return Ok(result);
        }

        [HttpGet("impersonations")]
        public async Task<ActionResult<List<ImpersonationSummaryDto>>> GetImpersonations(CancellationToken ct)
        {
            var result = await _securityQueryService.GetImpersonationsAsync(ct);
            return Ok(result);
        }
    }
}