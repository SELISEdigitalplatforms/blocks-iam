using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Services;
using Blocks.Genesis;
using Iam.DomainService.Activity.RequestModel;
using Iam.DomainService.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// Self-service security endpoints.
    /// All routes live under <c>/security</c>.
    /// </summary>
    [ApiController]
    [Route("security")]
    public class SecurityController : ControllerBase
    {
        private readonly ISecurityQueryService _securityQueryService;
        private readonly ISessionRevocationService _sessionRevocationService;
        private readonly IActivityQueryService _activityQueryService;

        public SecurityController(
            ISecurityQueryService securityQueryService,
            ISessionRevocationService sessionRevocationService,
            IActivityQueryService activityQueryService)
        {
            _securityQueryService = securityQueryService;
            _sessionRevocationService = sessionRevocationService;
            _activityQueryService = activityQueryService;
        }

        [HttpGet("summary")]
        [ProtectedEndPoint("blocks-iam::iam::security-audit")]
        public async Task<IActionResult> GetSummary([FromQuery] string? uid, CancellationToken ct)
        {
            var actor = ResolveActor(uid);
            if (string.IsNullOrWhiteSpace(actor))
            {
                return BadRequest(new { error = "user_id_required" });
            }
            var summary = await _securityQueryService.GetSecuritySummaryAsync(actor, ct);
            return Ok(summary);
        }

        [HttpGet("sessions")]
        [ProtectedEndPoint("blocks-iam::iam::security-audit")]
        public async Task<ActionResult<IReadOnlyList<UserSessionDto>>> GetUserSessions([FromQuery] string? uid, CancellationToken ct)
        {
            var actor = ResolveActor(uid);
            if (string.IsNullOrWhiteSpace(actor))
            {
                return BadRequest(new { error = "user_id_required" });
            }
            var sessions = await _securityQueryService.GetUserSessionsAsync(actor, ct);
            return Ok(sessions);
        }

        [HttpGet("sessions/{sessionId}")]
        [ProtectedEndPoint("blocks-iam::iam::security-audit")]
        public async Task<ActionResult<SessionDetailsDto>> GetSessionDetails(
            [FromRoute] string sessionId,
            [FromQuery] string? uid,
            CancellationToken ct)
        {
            var actor = ResolveActor(uid);
            if (string.IsNullOrWhiteSpace(actor))
            {
                return BadRequest(new { error = "user_id_required" });
            }

            var details = await _securityQueryService.GetSessionDetailsAsync(actor, sessionId, ct);
            if (details.Overview == null)
            {
                return NotFound(new { error = "session_not_found" });
            }
            return Ok(details);
        }

        [HttpPost("sessions/{sessionId}/revoke")]
        [ProtectedEndPoint("blocks-iam::iam::mutate-security-audit")]
        public async Task<IActionResult> RevokeSession(
            [FromRoute] string sessionId,
            [FromBody] RevokeSessionRequest? req,
            CancellationToken ct)
        {
            var actor = ResolveActor(req.UserId);
            var currentSessionId = await _securityQueryService.ResolveCurrentSessionIdAsync(actor, ct);

            var result = await _sessionRevocationService.RevokeSessionAsync(
                sessionId, actor, currentSessionId, req?.Reason, ct);

            if (result.Warnings.Contains("cannot_revoke_current_session"))
            {
                return BadRequest(new { error = "cannot_revoke_current_session", hint = "POST /auth/logout" });
            }

            return Ok(result);
        }

        [HttpPost("revoke/refresh-tokens/{tokenId}")]
        [ProtectedEndPoint("blocks-iam::iam::mutate-security-audit")]
        public async Task<IActionResult> RevokeRefreshToken(
            [FromRoute] string tokenId,
            [FromBody] RevokeSessionRequest? req,
            CancellationToken ct)
        {
            var actor = ResolveActor(req.UserId);
            var currentSessionId = await _securityQueryService.ResolveCurrentSessionIdAsync(actor, ct);

            var result = await _sessionRevocationService.RevokeRefreshTokenAsync(
                tokenId, actor, currentSessionId, req?.Reason, ct);

            if (result.Warnings.Contains("cannot_revoke_current_session"))
            {
                return BadRequest(new { error = "cannot_revoke_current_session", hint = "POST /auth/logout" });
            }

            return Ok(result);
        }

        [HttpPost("activity")]
        [ProtectedEndPoint("blocks-iam::iam::security-audit")]
        public async Task<IActionResult> GetActivity(
            [FromBody] GetActivityPayload payload,
            CancellationToken ct)
        {
            var actor = ResolveActor(payload?.UserId);
            if (string.IsNullOrWhiteSpace(actor))
            {
                return BadRequest(new { error = "user_id_required" });
            }

            var req = new GetActivitiesRequest
            {
                Page = payload.Page ?? 0,
                PageSize = payload.PageSize ?? 25,
                Filter = new GetActivitiesFilter
                {
                    SessionId = payload.SessionId,
                    ClientId = payload.ClientId,
                    Events = payload.Events,
                    Outcomes = payload.Outcomes,
                    Categories = payload.Categories,
                    From = payload.From,
                    To = payload.To,
                    Search = payload.Search,
                },
            };

            var response = await _activityQueryService.GetActivityPageAsync(actor, req, ct);
            return Ok(response);
        }

        public sealed class GetActivityPayload
        {
            public int? Page { get; set; }
            public int? PageSize { get; set; }
            public string? SessionId { get; set; }
            public string? ClientId { get; set; }
            public List<string>? Events { get; set; }
            public List<string>? Outcomes { get; set; }
            public List<UserActivityCategory>? Categories { get; set; }
            public DateTime? From { get; set; }
            public DateTime? To { get; set; }
            public string? Search { get; set; }
            public string? UserId { get; set; }
        }

        private static string ResolveActor(string? userIdOverride = null) =>
            !string.IsNullOrWhiteSpace(userIdOverride)
                ? userIdOverride
                : BlocksContext.GetContext()?.UserId ?? string.Empty;
    }
}