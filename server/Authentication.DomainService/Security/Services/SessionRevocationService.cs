using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Blocks.Genesis;
using Authentication.DomainService.Security.Models;
using Authentication.DomainService.Security.Repositories;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Security.Services
{
    public sealed class SessionRevocationService : ISessionRevocationService
    {
        // The key Genesis' RevokedSessions reads ("revoked_session:{sid}"). Services whose Genesis has the
        // revoked-session token check refuse an access token carrying a marked sid. Replace this with
        // RevokedSessions.MarkAsync once IAM is on a Genesis version that ships it.
        public const string RevokedSessionKeyPrefix = "revoked_session:";

        // Margin over the longest access-token lifetime, for clock skew between services.
        private const int RevokedMarkMarginMinutes = 5;

        private readonly ILogger<SessionRevocationService> _logger;
        private readonly ISecurityRepository _securityRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IIdpSessionRepository _idpSessionRepository;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICacheClient _cacheClient;
        private readonly IUserActivityDispatcher _userActivityDispatcher;

        public SessionRevocationService(
            ILogger<SessionRevocationService> logger,
            ISecurityRepository securityRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IIdpSessionRepository idpSessionRepository,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IUserActivityDispatcher userActivityDispatcher)
        {
            _logger = logger;
            _securityRepository = securityRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _idpSessionRepository = idpSessionRepository;
            _authenticationRepository = authenticationRepository;
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

            // A session with no client id (embedded login: password grant, no OIDC client) is still a live
            // session; only its status decides whether there is anything to sign out.
            if (session.Status != SessionStatus.Active)
            {
                // Its tokens are gone, but the IdP session behind single sign-on may still be alive and
                // would let that browser sign back in without credentials.
                await EndIdpSessionAsync(sessionId, response);
                response.AlreadyRevoked = true;
                response.RevokedAt = DateTime.UtcNow;
                return response;
            }

            // Read the live tokens before revoking them: afterwards they no longer match the active
            // filter, yet their cached copies must still be removed, because a refresh accepts a cached
            // token without checking the store.
            IReadOnlyList<RefreshTokenModel> liveTokens;
            try
            {
                liveTokens = await _refreshTokenRepository.GetActiveTokensBySessionIdAsync(sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read live refresh tokens for session {SessionId}", sessionId);
                liveTokens = Array.Empty<RefreshTokenModel>();
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
                foreach (var token in liveTokens)
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
                response.Warnings.Add("refresh_token_cache_remove_failed");
            }

            await EndIdpSessionAsync(sessionId, response);

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

        // Ends the device's sign-in, not just its current tokens: revokes the IdP session that single
        // sign-on uses to issue a new code silently, and marks the session id so services that check
        // revoked sessions refuse its still-valid access tokens. Each step is best effort and reports a
        // warning instead of failing the sign-out.
        private async Task EndIdpSessionAsync(string sessionId, RevokeSessionResponse response)
        {
            try
            {
                await _idpSessionRepository.RevokeAsync(sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to revoke the IdP session {SessionId}", sessionId);
                response.Warnings.Add("idp_session_revoke_failed");
            }

            try
            {
                var lifetime = await RevokedMarkLifetimeAsync();
                await _cacheClient.AddStringValueAsync(RevokedSessionKeyPrefix + sessionId, "1", (long)lifetime.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark session {SessionId} as revoked", sessionId);
                response.Warnings.Add("revoked_session_mark_failed");
            }
        }

        // The mark must outlive every access token that can carry this session id: the project's own
        // lifetime or the per-client maximum, whichever is longer.
        private async Task<TimeSpan> RevokedMarkLifetimeAsync()
        {
            var projectMinutes = IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes;
            try
            {
                var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
                projectMinutes = configuration?.AccessTokenValidForNumberMinutes ?? projectMinutes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read the access-token lifetime; using the per-client maximum");
            }

            return TimeSpan.FromMinutes(Math.Max(projectMinutes, OidcClientTokenLifetimes.MaxAccessTokenMinutes) + RevokedMarkMarginMinutes);
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
