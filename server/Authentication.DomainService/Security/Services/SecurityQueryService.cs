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

            await PopulateRotationMetadataAsync(items, ct);

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

                var rotations = await _securityRepository.GetRotationHistoryAsync(dto.SessionId ?? string.Empty, ct);
                dto.RotationCount = rotations.Count;
                dto.LastRotatedAt = rotations.Count > 0 ? rotations.Max(r => r.AbsoluteExpiry) : (DateTime?)null;
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

        private async Task PopulateRotationMetadataAsync(IList<SessionDto> sessions, CancellationToken ct)
        {
            foreach (var s in sessions)
            {
                if (string.IsNullOrEmpty(s.SessionId))
                {
                    continue;
                }
                try
                {
                    var rotations = await _securityRepository.GetRotationHistoryAsync(s.SessionId, ct);
                    s.RotationCount = rotations.Count;
                    s.LastRotatedAt = rotations.Count > 0 ? rotations.Max(r => r.AbsoluteExpiry) : (DateTime?)null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to populate rotation metadata for session {SessionId}", s.SessionId);
                }
            }
        }
    }
}