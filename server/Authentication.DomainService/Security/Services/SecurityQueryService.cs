using Blocks.Genesis;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Security.Contracts;
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
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SecurityQueryService(
            ILogger<SecurityQueryService> logger,
            ISecurityRepository securityRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IHttpContextAccessor httpContextAccessor)
        {
            _logger = logger;
            _securityRepository = securityRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<SecurityOverviewDto> GetSecurityOverviewAsync(string userId, CancellationToken ct)
        {
            var overview = new SecurityOverviewDto();

            if (string.IsNullOrWhiteSpace(userId))
            {
                return overview;
            }

            var currentSessionId = await ResolveCurrentSessionIdAsync(userId, ct);
            overview.CurrentSessionId = currentSessionId;

            var groupsTask = GetSessionGroupsAsync(userId, ct);
            var idpSessionTask = _securityRepository.GetIdpSessionAsync(userId, ResolveTenantId(), ct);

            await Task.WhenAll(groupsTask, idpSessionTask);

            var groups = groupsTask.Result;
            foreach (var group in groups)
            {
                group.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == group.SessionId;
            }

            overview.SessionGroups = groups;
            overview.IdpSession = idpSessionTask.Result;

            return overview;
        }

        public async Task<SessionTimelineDto> GetSessionTimelineAsync(string userId, string sessionId, CancellationToken ct)
        {
            var timeline = new SessionTimelineDto { SessionId = sessionId };

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return timeline;
            }

            var group = await _securityRepository.GetSessionGroupAsync(userId, sessionId, ct);
            if (group == null)
            {
                return timeline;
            }

            var currentSessionId = await ResolveCurrentSessionIdAsync(userId, ct);
            group.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == group.SessionId;
            timeline.Session = group;

            var activeApp = group.Apps.FirstOrDefault(a => a.IsActive) ?? group.Apps.FirstOrDefault();
            if (activeApp != null && !string.IsNullOrWhiteSpace(activeApp.TokenId))
            {
                timeline.RefreshTokenStatus = await _securityRepository.GetRefreshTokenStatusAsync(activeApp.TokenId, ct);
            }

            timeline.RevokedAccessTokens = (await _securityRepository.GetRevokedAccessTokensAsync(userId, ct)).ToList();
            timeline.Rotations = (await _securityRepository.GetRotationHistoryAsync(sessionId, ct)).ToList();

            // TODO: Lifecycle from UserActivityEvent stream once a session-indexed reader exists.
            timeline.Lifecycle = [];

            return timeline;
        }

        public async Task<IReadOnlyList<SessionGroupDto>> GetSessionGroupsAsync(string userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return [];
            }

            var items = await _securityRepository.GetSessionGroupsAsync(userId, null, false, ct);
            var currentSessionId = await ResolveCurrentSessionIdAsync(userId, ct);
            foreach (var item in items)
            {
                item.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == item.SessionId;
            }
            return items;
        }

        public async Task<SessionGroupDto?> GetSessionGroupAsync(string userId, string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var dto = await _securityRepository.GetSessionGroupAsync(userId, sessionId, ct);
            if (dto != null)
            {
                var currentSessionId = await ResolveCurrentSessionIdAsync(userId, ct);
                dto.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == dto.SessionId;
            }
            return dto;
        }

        public async Task<IReadOnlyList<RefreshTokenRotationDto>> GetRotationHistoryAsync(string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Array.Empty<RefreshTokenRotationDto>();
            }
            return await _securityRepository.GetRotationHistoryAsync(sessionId, ct);
        }

        private static string? ResolveTenantId() => BlocksContext.GetContext()?.TenantId;

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

                // NOTE: cookie name is hardcoded; if the production cookie name differs,
                // currentSessionId will silently return null.
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