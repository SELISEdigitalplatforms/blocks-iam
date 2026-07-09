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

        public async Task<BaseQueryListResponse<IQueryable<SessionDto>>> GetSessionsAsync(GetSessionsRequest req, CancellationToken ct)
        {
            var userId = ResolveUserId();
            var tenantId = ResolveTenantId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new BaseQueryListResponse<IQueryable<SessionDto>>
                {
                    Data = Enumerable.Empty<SessionDto>().AsQueryable(),
                    TotalCount = 0
                };
            }

            var currentSessionId = await ResolveCurrentSessionIdAsync(userId, tenantId, ct);

            var items = (await _securityRepository.GetSessionsAsync(userId, tenantId, req, ct)).ToList();
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

        public async Task<SessionDto?> GetSessionAsync(string sessionId, CancellationToken ct)
        {
            var userId = ResolveUserId();
            var tenantId = ResolveTenantId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            var dto = await _securityRepository.GetSessionAsync(userId, tenantId, sessionId, ct);
            if (dto != null)
            {
                var currentSessionId = await ResolveCurrentSessionIdAsync(userId, tenantId, ct);
                dto.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == dto.SessionId;
            }
            return dto;
        }

        public async Task<BaseQueryListResponse<IQueryable<AuthHistoryDto>>> GetHistoryAsync(GetHistoryRequest req, CancellationToken ct)
        {
            var userId = ResolveUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new BaseQueryListResponse<IQueryable<AuthHistoryDto>>
                {
                    Data = Enumerable.Empty<AuthHistoryDto>().AsQueryable(),
                    TotalCount = 0
                };
            }

            var items = (await _securityRepository.GetHistoryAsync(userId, req, ct)).ToList();
            return new BaseQueryListResponse<IQueryable<AuthHistoryDto>>
            {
                Data = items.AsQueryable(),
                TotalCount = items.Count
            };
        }

        public async Task<SessionTimelineDto?> GetSessionTimelineAsync(string sessionId, CancellationToken ct)
        {
            var userId = ResolveUserId();
            var tenantId = ResolveTenantId();
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var session = await _securityRepository.GetSessionAsync(userId, tenantId, sessionId, ct);
            if (session == null)
            {
                return null;
            }

            var currentSessionId = await ResolveCurrentSessionIdAsync(userId, tenantId, ct);
            session.IsCurrent = !string.IsNullOrEmpty(currentSessionId) && currentSessionId == session.SessionId;

            var refreshStatus = await _securityRepository.GetRefreshTokenStatusAsync(sessionId, ct);
            var revokedAccess = await _securityRepository.GetRevokedAccessTokensAsync(userId, ct);
            var lifecycle = await _securityRepository.GetSessionLifecycleAsync(userId, sessionId, ct);

            return new SessionTimelineDto
            {
                SessionId = sessionId,
                Session = session,
                RefreshTokenStatus = refreshStatus,
                RevokedAccessTokens = revokedAccess.ToList(),
                Lifecycle = lifecycle.ToList()
            };
        }

        public async Task<IdpSessionSummaryDto?> GetIdpSessionAsync(CancellationToken ct)
        {
            var userId = ResolveUserId();
            var tenantId = ResolveTenantId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }
            return await _securityRepository.GetIdpSessionAsync(userId, tenantId, ct);
        }

        public async Task<List<ImpersonationSummaryDto>> GetImpersonationsAsync(CancellationToken ct)
        {
            var userId = ResolveUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new List<ImpersonationSummaryDto>();
            }
            var items = await _securityRepository.GetImpersonationsAsync(userId, ct);
            return items.ToList();
        }

        private static string? ResolveUserId() => BlocksContext.GetContext()?.UserId;
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