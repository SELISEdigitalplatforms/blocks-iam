using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Blocks.Genesis;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Repositories;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Security.Services
{
    public sealed class SessionRevocationService : ISessionRevocationService
    {
        private readonly ILogger<SessionRevocationService> _logger;
        private readonly ISecurityRepository _securityRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IUserActivityDispatcher _userActivityDispatcher;

        public SessionRevocationService(
            ILogger<SessionRevocationService> logger,
            ISecurityRepository securityRepository,
            IRefreshTokenRepository refreshTokenRepository,
            ICacheClient cacheClient,
            IUserActivityDispatcher userActivityDispatcher)
        {
            _logger = logger;
            _securityRepository = securityRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _cacheClient = cacheClient;
            _userActivityDispatcher = userActivityDispatcher;
        }

        public async Task<RevokeSessionResponse> RevokeSessionAsync(string sessionId, string actorUserId, string currentSessionId, string? reason, CancellationToken ct)
        {
            var response = new RevokeSessionResponse
            {
                SessionId = sessionId,
                Reason = string.IsNullOrWhiteSpace(reason) ? "user_revoked" : reason
            };

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return response;
            }

            var tenantId = BlocksContext.GetContext()?.TenantId;
            var session = await _securityRepository.GetUserSessionAsync(actorUserId, sessionId, ct);

            if (session == null)
            {
                return response;
            }

            if (!string.IsNullOrEmpty(currentSessionId) && sessionId == currentSessionId)
            {
                response.Warnings.Add("cannot_revoke_current_session");
                return response;
            }

            if (session.Status != SessionStatus.Active || session.ClientIds.Count == 0)
            {
                response.AlreadyRevoked = true;
                response.RevokedAt = DateTime.UtcNow;
                return response;
            }

            int revokedRefreshTokens;
            try
            {
                revokedRefreshTokens = await _refreshTokenRepository.RevokeAllBySessionIdAsync(sessionId, response.Reason!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to revoke refresh tokens for session {SessionId}", sessionId);
                response.Warnings.Add("refresh_token_revoke_skipped:db_unreachable");
                revokedRefreshTokens = 0;
            }

            try
            {
                var activeTokens = await _refreshTokenRepository.GetActiveTokensBySessionIdAsync(sessionId);
                foreach (var token in activeTokens)
                {
                    if (!string.IsNullOrWhiteSpace(token.TokenId))
                    {
                        await _cacheClient.RemoveKeyAsync(token.TokenId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove refresh-token cache keys for session {SessionId}", sessionId);
            }

            response.RevokedRefreshTokens = revokedRefreshTokens;
            response.AlreadyRevoked = false;
            response.RevokedAt = DateTime.UtcNow;
            response.ClientId = session.ClientIds.FirstOrDefault();

            try
            {
                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = actorUserId,
                    TenantId = tenantId,
                    Category = UserActivityCategory.Auth,
                    Event = SessionAuditEvents.UserRevokedSession,
                    Source = "auth-session-revocation",
                    SessionId = sessionId,
                    ClientId = response.ClientId,
                    Outcome = "success",
                    ReasonCode = response.Reason,
                    Context = new ActivityContext
                    {
                        IpAddress = session.PrimaryIpAddress
                    },
                    Metadata = new Dictionary<string, string> { { "actionBy", actorUserId } }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish user_revoked_session UserActivity event for session {SessionId}", sessionId);
                response.Warnings.Add("timeline_event_publish_failed");
            }

            return response;
        }

        public async Task<RevokeSessionResponse> RevokeRefreshTokenAsync(string tokenId, string actorUserId, string currentSessionId, string? reason, CancellationToken ct)
        {
            var response = new RevokeSessionResponse
            {
                Reason = string.IsNullOrWhiteSpace(reason) ? "user_revoked" : reason
            };

            if (string.IsNullOrWhiteSpace(tokenId))
            {
                return response;
            }

            var active = await _refreshTokenRepository.GetByTokenIdAsync(tokenId);
            if (active == null)
            {
                response.Warnings.Add("refresh_token_not_found");
                return response;
            }

            if (!string.IsNullOrEmpty(currentSessionId) && active.SessionId == currentSessionId)
            {
                response.SessionId = active.SessionId;
                response.Warnings.Add("cannot_revoke_current_session");
                return response;
            }

            response.SessionId = active.SessionId;
            response.ClientId = active.ClientId;

            try
            {
                var ok = await _refreshTokenRepository.RevokeByTokenIdAsync(tokenId, response.Reason!);
                if (ok)
                {
                    response.RevokedRefreshTokens = 1;
                    response.RevokedAt = DateTime.UtcNow;
                }
                await _cacheClient.RemoveKeyAsync(tokenId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to revoke refresh token {TokenFingerprint}", tokenId.Length <= 8 ? tokenId : tokenId.Substring(0, 8));
                response.Warnings.Add("refresh_token_revoke_skipped:db_unreachable");
            }

            try
            {
                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = actorUserId,
                    TenantId = BlocksContext.GetContext()?.TenantId,
                    Category = UserActivityCategory.Auth,
                    Event = SessionAuditEvents.UserRevokedSession,
                    Source = "auth-refresh-token-revocation",
                    SessionId = active.SessionId,
                    ClientId = active.ClientId,
                    Outcome = "success",
                    ReasonCode = response.Reason,
                    Context = new ActivityContext
                    {
                        IpAddress = active.IpAddress
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "actionBy", actorUserId },
                        { "scope", "single_token" }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish refresh-token revocation activity event");
                response.Warnings.Add("timeline_event_publish_failed");
            }

            return response;
        }
    }
}
