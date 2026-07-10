using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Services;
using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// Self-service security endpoints.
    /// All routes live under <c>/security</c>.
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

        [HttpGet("overview")]
        public async Task<ActionResult<SecurityOverviewDto>> GetOverview(CancellationToken ct)
        {
            var actorUserId = ResolveActorUserId();
            var result = await _securityQueryService.GetSecurityOverviewAsync(actorUserId, ct);
            return Ok(result);
        }

        [HttpGet("sessions/{sessionId}")]
        public async Task<ActionResult<SessionTimelineDto>> GetSessionTimeline(
            [FromRoute] string sessionId,
            CancellationToken ct)
        {
            var actorUserId = ResolveActorUserId();
            var timeline = await _securityQueryService.GetSessionTimelineAsync(actorUserId, sessionId, ct);
            if (timeline.Session == null)
            {
                return NotFound(new { error = "session_not_found" });
            }
            return Ok(timeline);
        }

        [HttpPost("sessions/{sessionId}/revoke")]
        public async Task<ActionResult<RevokeSessionResponse>> RevokeSession(
            [FromRoute] string sessionId,
            [FromBody] RevokeSessionRequest? req,
            CancellationToken ct)
        {
            var actorUserId = ResolveActorUserId();
            var currentSessionId = await _securityQueryService.ResolveCurrentSessionIdAsync(actorUserId, ct);

            var result = await _sessionRevocationService.RevokeSessionAsync(
                sessionId, actorUserId, currentSessionId, req?.Reason, ct);

            if (result.Warnings.Contains("cannot_revoke_current_session"))
            {
                return BadRequest(new { error = "cannot_revoke_current_session", hint = "POST /auth/logout" });
            }

            return Ok(result);
        }

        [HttpPost("revoke/refresh-tokens/{tokenId}")]
        public async Task<ActionResult<RevokeSessionResponse>> RevokeRefreshToken(
            [FromRoute] string tokenId,
            [FromBody] RevokeSessionRequest? req,
            CancellationToken ct)
        {
            var actorUserId = ResolveActorUserId();
            var currentSessionId = await _securityQueryService.ResolveCurrentSessionIdAsync(actorUserId, ct);

            var result = await _sessionRevocationService.RevokeRefreshTokenAsync(
                tokenId, actorUserId, currentSessionId, req?.Reason, ct);

            if (result.Warnings.Contains("cannot_revoke_current_session"))
            {
                return BadRequest(new { error = "cannot_revoke_current_session", hint = "POST /auth/logout" });
            }

            return Ok(result);
        }

        private string ResolveActorUserId() => BlocksContext.GetContext()?.UserId ?? string.Empty;
    }
}