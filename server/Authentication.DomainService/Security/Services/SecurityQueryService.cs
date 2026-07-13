using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Security.Services
{
    public sealed class SecurityQueryService : ISecurityQueryService
    {
        private readonly ILogger<SecurityQueryService> _logger;
        private readonly ISecurityRepository _securityRepository;
        private readonly IActivityQueryService _activityQueryService;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SecurityQueryService(
            ILogger<SecurityQueryService> logger,
            ISecurityRepository securityRepository,
            IActivityQueryService activityQueryService,
            IRefreshTokenRepository refreshTokenRepository,
            IHttpContextAccessor httpContextAccessor)
        {
            _logger = logger;
            _securityRepository = securityRepository;
            _activityQueryService = activityQueryService;
            _refreshTokenRepository = refreshTokenRepository;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<SecuritySummaryDto> GetSecuritySummaryAsync(string userId, CancellationToken ct)
        {
            var summary = new SecuritySummaryDto();

            if (string.IsNullOrWhiteSpace(userId))
            {
                return summary;
            }

            var currentSessionId = await ResolveCurrentSessionIdAsync(userId, ct);
            summary.CurrentSessionId = currentSessionId;

            var sessions = await GetUserSessionsAsync(userId, ct);
            summary.TotalSessions = sessions.Count;
            summary.ActiveSessions = sessions.Count(s => s.Status == SessionStatus.Active);
            summary.ExpiredSessions = sessions.Count(s => s.Status == SessionStatus.Expired);
            summary.RevokedSessions = sessions.Count(s => s.Status == SessionStatus.Revoked);
            summary.LastActivityAt = sessions
                .Select(s => (DateTime?)s.LastActivityAt)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .DefaultIfEmpty()
                .Max();
            summary.LastLoginAt = sessions
                .Select(s => (DateTime?)s.CreatedAt)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .DefaultIfEmpty()
                .Max();

            return summary;
        }

        public async Task<IReadOnlyList<UserSessionDto>> GetUserSessionsAsync(string userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return [];
            }

            var sessions = await _securityRepository.GetUserSessionsAsync(userId, null, false, ct);
            var currentSessionId = await ResolveCurrentSessionIdAsync(userId, ct);
            foreach (var s in sessions)
            {
                s.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == s.SessionId;
            }
            return sessions;
        }

        public async Task<SessionDetailsDto> GetSessionDetailsAsync(
            string userId,
            string sessionId,
            CancellationToken ct)
        {
            var details = new SessionDetailsDto();

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return details;
            }

            var session = await _securityRepository.GetUserSessionAsync(userId, sessionId, ct);
            if (session == null)
            {
                return details;
            }

            var currentSessionId = await ResolveCurrentSessionIdAsync(userId, ct);
            session.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == session.SessionId;

            details.Overview = new SessionOverviewDto
            {
                SessionId = session.SessionId,
                Status = session.Status,
                IsCurrent = session.IsCurrent,
                DeviceName = session.PrimaryDeviceName,
                DeviceModel = null,
                OperatingSystem = session.PrimaryOperatingSystem,
                Browser = session.PrimaryBrowser,
                IpAddress = session.PrimaryIpAddress,
                ClientIds = session.ClientIds,
                OrganizationIds = [],
                StartedAt = session.CreatedAt,
                LastActivityAt = session.LastActivityAt,
                AbsoluteExpiry = session.AbsoluteExpiry,
                IdleExpiry = session.IdleExpiry,
            };

            details.Applications = BuildApplications(session);
            details.Timeline = await BuildTimelineAsync(userId, session, ct);

            return details;
        }

        private static List<ApplicationDto> BuildApplications(UserSessionDto session)
        {
            return session.ClientIds
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(clientId => new ApplicationDto
                {
                    ClientId = clientId,
                    Status = session.Status,
                    ExpiresAt = session.AbsoluteExpiry,
                    RotationCount = 1,
                    LastRotationAt = session.CreatedAt,
                })
                .ToList();
        }

        private async Task<List<TimelineEventDto>> BuildTimelineAsync(
            string userId,
            UserSessionDto session,
            CancellationToken ct)
        {
            var timeline = new List<TimelineEventDto>();

            var activities = await _activityQueryService.GetActivitiesForSessionAsync(
                userId,
                session.SessionId,
                0,
                50,
                ct);

            foreach (var activity in activities)
            {
                timeline.Add(new TimelineEventDto
                {
                    Type = TimelineEventType.Auth,
                    At = activity.CreatedDate,
                    Event = activity.Event,
                    Outcome = activity.Outcome,
                    ReasonCode = activity.ReasonCode,
                    IpAddress = activity.Context?.IpAddress,
                    DeviceName = activity.Context?.DeviceName,
                    ClientId = activity.ClientId,
                });
            }

            var rotations = await _securityRepository.GetRotationHistoryAsync(session.SessionId, ct);
            foreach (var rotation in rotations)
            {
                timeline.Add(new TimelineEventDto
                {
                    Type = TimelineEventType.Refresh,
                    At = rotation.IssuedUtc,
                    ClientId = rotation.ClientId,
                });

                if (rotation.IsRevoked && rotation.RevokedAt.HasValue)
                {
                    timeline.Add(new TimelineEventDto
                    {
                        Type = TimelineEventType.Revocation,
                        At = rotation.RevokedAt.Value,
                        ClientId = rotation.ClientId,
                        ReasonCode = rotation.RevokeReason,
                    });
                }
            }

            return timeline
                .OrderBy(t => t.At)
                .ThenBy(t => t.Type)
                .ToList();
        }

        public async Task<string?> ResolveCurrentSessionIdAsync(string userId, CancellationToken ct)
        {
            try
            {
                var http = _httpContextAccessor.HttpContext;
                if (http == null || string.IsNullOrWhiteSpace(userId))
                {
                    return null;
                }

                var cookieKey = $"tetorefreshtoken_{http.Request.Host.Host}";
                var refreshToken = http.Request.Cookies[cookieKey];
                if (string.IsNullOrEmpty(refreshToken))
                {
                    refreshToken = http.Request.Headers[cookieKey];
                }
                if (string.IsNullOrEmpty(refreshToken))
                {
                    return null;
                }

                var active = await _refreshTokenRepository.GetByTokenIdAsync(refreshToken!);
                return active?.SessionId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve current session id from request.");
                return null;
            }
        }
    }
}
