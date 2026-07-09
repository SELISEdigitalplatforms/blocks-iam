using Blocks.Genesis;
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
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SecurityQueryService(
            ILogger<SecurityQueryService> logger,
            ISecurityRepository securityRepository,
            IHttpContextAccessor httpContextAccessor)
        {
            _logger = logger;
            _securityRepository = securityRepository;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<BaseQueryListResponse<IQueryable<SessionDto>>> GetSessionsAsync(string targetUserId, GetSessionsRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(targetUserId))
            {
                return new BaseQueryListResponse<IQueryable<SessionDto>>
                {
                    Data = Enumerable.Empty<SessionDto>().AsQueryable(),
                    TotalCount = 0
                };
            }

            var tenantId = ResolveTenantId();
            var currentSessionId = await ResolveCurrentSessionIdAsync(targetUserId, tenantId, ct);

            var items = (await _securityRepository.GetSessionsAsync(targetUserId, tenantId, req, ct)).ToList();
            foreach (var item in items)
            {
                item.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == item.SessionId;
            }

            return new BaseQueryListResponse<IQueryable<SessionDto>>
            {
                Data = items.AsQueryable(),
                TotalCount = items.Count
            };
        }

        public async Task<SessionDto?> GetSessionAsync(string targetUserId, string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(targetUserId))
            {
                return null;
            }

            var tenantId = ResolveTenantId();
            var dto = await _securityRepository.GetSessionAsync(targetUserId, tenantId, sessionId, ct);
            if (dto != null)
            {
                var currentSessionId = await ResolveCurrentSessionIdAsync(targetUserId, tenantId, ct);
                dto.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == dto.SessionId;
            }
            return dto;
        }

        public async Task<SessionDto?> GetSessionByIdAsync(string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }
            return await _securityRepository.GetSessionByIdAsync(sessionId, ct);
        }

        public async Task<BaseQueryListResponse<IQueryable<AuthHistoryDto>>> GetHistoryAsync(string targetUserId, GetHistoryRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(targetUserId))
            {
                return new BaseQueryListResponse<IQueryable<AuthHistoryDto>>
                {
                    Data = Enumerable.Empty<AuthHistoryDto>().AsQueryable(),
                    TotalCount = 0
                };
            }

            var items = (await _securityRepository.GetHistoryAsync(targetUserId, req, ct)).ToList();
            return new BaseQueryListResponse<IQueryable<AuthHistoryDto>>
            {
                Data = items.AsQueryable(),
                TotalCount = items.Count
            };
        }

        public async Task<SessionTimelineDto?> GetSessionTimelineAsync(string targetUserId, string sessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(targetUserId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var tenantId = ResolveTenantId();
            var session = await _securityRepository.GetSessionAsync(targetUserId, tenantId, sessionId, ct);
            if (session == null)
            {
                return null;
            }

            var currentSessionId = await ResolveCurrentSessionIdAsync(targetUserId, tenantId, ct);
            session.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == session.SessionId;

            var refreshStatus = await _securityRepository.GetRefreshTokenStatusAsync(sessionId, ct);
            var revokedAccess = await _securityRepository.GetRevokedAccessTokensAsync(targetUserId, ct);
            var lifecycle = await _securityRepository.GetSessionLifecycleAsync(targetUserId, sessionId, ct);

            return new SessionTimelineDto
            {
                SessionId = sessionId,
                Session = session,
                RefreshTokenStatus = refreshStatus,
                RevokedAccessTokens = revokedAccess.ToList(),
                Lifecycle = lifecycle.ToList()
            };
        }

        public async Task<IdpSessionSummaryDto?> GetIdpSessionAsync(string targetUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(targetUserId))
            {
                return null;
            }
            var tenantId = ResolveTenantId();
            return await _securityRepository.GetIdpSessionAsync(targetUserId, tenantId, ct);
        }

        public async Task<List<ImpersonationSummaryDto>> GetImpersonationsAsync(string targetUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(targetUserId))
            {
                return new List<ImpersonationSummaryDto>();
            }
            var items = await _securityRepository.GetImpersonationsAsync(targetUserId, ct);
            return items.ToList();
        }

        private static string? ResolveTenantId() => BlocksContext.GetContext()?.TenantId;

        private async Task<string?> ResolveCurrentSessionIdAsync(string userId, string? tenantId, CancellationToken ct)
        {
            try
            {
                var http = _httpContextAccessor.HttpContext;
                if (http == null)
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

                var dto = await _securityRepository.GetSessionByRefreshTokenAsync(userId, tenantId, refreshToken!, ct);
                return dto?.SessionId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve current session id from request.");
                return null;
            }
        }

        public async Task<string?> ResolveCurrentSessionIdAsync(string userId, CancellationToken ct)
        {
            return await ResolveCurrentSessionIdAsync(userId, ResolveTenantId(), ct);
        }
    }
}