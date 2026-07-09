using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Iam.DomainService.Utilities;
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
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ITokenRevocationRepository _tokenRevocationRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;

        public SessionRevocationService(
            ILogger<SessionRevocationService> logger,
            ISecurityRepository securityRepository,
            IAuthenticationRepository authenticationRepository,
            IRefreshTokenRepository refreshTokenRepository,
            ITokenRevocationRepository tokenRevocationRepository,
            IAuthenticationDomainService authenticationDomainService)
        {
            _logger = logger;
            _securityRepository = securityRepository;
            _authenticationRepository = authenticationRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _tokenRevocationRepository = tokenRevocationRepository;
            _authenticationDomainService = authenticationDomainService;
        }

        public async Task<RevokeSessionResponse> RevokeSessionAsync(string sessionId, string actorUserId, string currentSessionId, string? targetUserId, string? reason, CancellationToken ct)
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
            var effectiveUserId = string.IsNullOrWhiteSpace(targetUserId) ? actorUserId : targetUserId!;
            var session = await _securityRepository.GetSessionAsync(effectiveUserId, tenantId, sessionId, ct);

            if (session == null)
            {
                return response;
            }

            if (!string.IsNullOrEmpty(currentSessionId) && sessionId == currentSessionId)
            {
                response.Warnings.Add("cannot_revoke_current_session");
                return response;
            }

            if (!session.IsActive)
            {
                response.AlreadyRevoked = true;
                response.RevokedAt = session.ExpiresUtc;
                return response;
            }

            var revoked = await _authenticationRepository.RevokeIdentitySessionsBySessionIdsAsync(new[] { sessionId });
            if (!revoked)
            {
                response.Warnings.Add("identity_session_revoke_failed");
            }

            var revokedRefreshTokens = 0;
            try
            {
                var tokens = (await _refreshTokenRepository.GetBySessionIdAsync(sessionId)).ToList();
                foreach (var token in tokens.Where(t => !t.IsRevoked))
                {
                    var ok = await _refreshTokenRepository.RevokeByTokenIdAsync(token.TokenId, response.Reason!);
                    if (ok)
                    {
                        revokedRefreshTokens++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to revoke refresh tokens for session {SessionId}", sessionId);
                response.Warnings.Add("refresh_token_revoke_skipped:db_unreachable");
            }

            response.RevokedRefreshTokens = revokedRefreshTokens;
            response.AlreadyRevoked = false;
            response.RevokedAt = DateTime.UtcNow;

            try
            {
                var timelineEvent = new UserAuthenticationTimelineEvent
                {
                    UserId = actorUserId,
                    TenantId = tenantId,
                    SessionId = sessionId,
                    ClientId = session.ClientId,
                    IpAddresses = session.IpAddresses,
                    DeviceInformation = null,
                    Event = SessionAuditEvents.UserRevokedSession,
                    ActionBy = actorUserId,
                    ReasonCode = response.Reason,
                    Outcome = "success"
                };

                await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, timelineEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish user_revoked_session timeline event for session {SessionId}", sessionId);
                response.Warnings.Add("timeline_event_publish_failed");
            }

            return response;
        }
    }
}