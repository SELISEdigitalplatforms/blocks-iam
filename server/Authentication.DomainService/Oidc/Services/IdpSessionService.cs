using Idp.DomainService.Oidc.Contracts;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Services
{
    /// <summary>
    /// IdP Session Service
    /// Implements OIDC multi-account SSO (single sign-on)
    /// Allows users to be logged in to multiple accounts under one session
    /// SOLID: Single Responsibility - session management only
    /// </summary>
    public interface IIdpSessionService
    {
        Task<string> CreateSessionAsync(string userId, string tenantId, string ipAddress);
        Task<IdpSessionModel> GetSessionAsync(string sessionId);
        Task<bool> AddAccountAsync(string sessionId, string userId, string tenantId, string displayName);
        Task<bool> SelectAccountAsync(string sessionId, string userId);
        Task<bool> RemoveAccountAsync(string sessionId, string userId, string? tenantId = null);
        Task<IEnumerable<IdpSessionAccount>> GetAccountsAsync(string sessionId);
        Task<bool> UpdateActivityAsync(string sessionId);
        Task<string?> RotateSessionAsync(string sessionId, string reason);
        Task<bool> RevokeSessionAsync(string sessionId, string reason);
        Task<bool> IsSessionActiveAsync(string sessionId);
        Task<IEnumerable<IdpSessionModel>> GetUserSessionsAsync(string userId, string tenantId);
    }

    public sealed class IdpSessionService : IIdpSessionService
    {
        private readonly IIdpSessionRepository _sessionRepo;
        private readonly IAuditLogRepository _auditLogRepo;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepo;
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<IdpSessionService> _logger;

        public IdpSessionService(
            IIdpSessionRepository sessionRepo,
            IAuditLogRepository auditLogRepo,
            IAuthenticationRepository authenticationRepository,
            IRefreshTokenRepository refreshTokenRepo,
            ICacheClient cacheClient,
            ILogger<IdpSessionService> logger)
        {
            _sessionRepo = sessionRepo;
            _auditLogRepo = auditLogRepo;
            _authenticationRepository = authenticationRepository;
            _refreshTokenRepo = refreshTokenRepo;
            _cacheClient = cacheClient;
            _logger = logger;
        }

        /// <summary>
        /// Create new IdP session for user
        /// </summary>
        public async Task<string> CreateSessionAsync(string userId, string tenantId, string ipAddress)
        {
            try
            {
                var session = new IdpSessionModel
                {
                    SessionId = GenerateSessionId(),
                    TenantId = tenantId,
                    Accounts = new List<IdpSessionAccount>
                    {
                        new IdpSessionAccount
                        {
                            UserId = userId,
                            TenantId = tenantId,
                            LoginAt = DateTime.UtcNow
                        }
                    },
                    IpAddress = ipAddress,
                    CreatedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                    IdleExpiry = DateTime.UtcNow.Add(GetIdpSessionIdleTimeout()),
                    AbsoluteExpiry = DateTime.UtcNow.Add(GetIdpSessionAbsoluteTimeout())
                };

                await _sessionRepo.CreateAsync(session);
                _logger.LogInformation("IdP session created: {SessionId}, user: {UserId}", session.SessionId, userId);
                await LogSessionEvent(SessionAuditEvents.SessionCreated, userId, session.SessionId);

                return session.SessionId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating IdP session");
                throw;
            }
        }

        /// <summary>
        /// Get session details
        /// </summary>
        public async Task<IdpSessionModel> GetSessionAsync(string sessionId)
        {
            try
            {
                return await _sessionRepo.GetBySessionIdAsync(sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting session: {sessionId}");
                throw;
            }
        }

        /// <summary>
        /// Add new account to existing session (multi-account SSO)
        /// User can log in with different account and it gets added to same session
        /// </summary>
        public async Task<bool> AddAccountAsync(string sessionId, string userId, string tenantId, string displayName)
        {
            try
            {
                var session = await _sessionRepo.GetBySessionIdAsync(sessionId);
                if (session == null || session.RevokedAt.HasValue)
                {
                    _logger.LogWarning("Cannot add account to invalid/revoked session: {SessionId}", sessionId);
                    return false;
                }

                // Check if account already exists
                if (session.Accounts.Any(a => a.UserId == userId && a.TenantId == tenantId))
                {
                    _logger.LogInformation("Account already exists in session: {UserId}", userId);
                    return true;
                }

                var newAccount = new IdpSessionAccount
                {
                    UserId = userId,
                    TenantId = tenantId,
                    LoginAt = DateTime.UtcNow,
                    DisplayName = displayName ?? userId
                };

                var success = await _sessionRepo.AddAccountAsync(sessionId, newAccount);
                if (success)
                {
                    _logger.LogInformation("Account added to session: {SessionId}, user: {UserId}", sessionId, userId);
                    await LogSessionEvent(SessionAuditEvents.AccountAdded, userId, sessionId);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding account to session: {sessionId}");
                throw;
            }
        }

        /// <summary>
        /// Select which account to use in this session
        /// (For display purposes - determines which user's context to use)
        /// </summary>
        public async Task<bool> SelectAccountAsync(string sessionId, string userId)
        {
            try
            {
                var session = await _sessionRepo.GetBySessionIdAsync(sessionId);
                if (session == null)
                {
                    return false;
                }

                // Check account exists in session
                if (!session.Accounts.Any(a => a.UserId == userId))
                {
                    _logger.LogWarning("Account not found in session: {UserId}", userId);
                    return false;
                }

                // Update activity to set selected account
                var success = await _sessionRepo.UpdateActivityAsync(sessionId);
                if (success)
                {
                    _logger.LogInformation("Account selected in session: {SessionId}, user: {UserId}", sessionId, userId);
                    await LogSessionEvent(SessionAuditEvents.AccountSelected, userId, sessionId);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error selecting account: {userId}");
                throw;
            }
        }

        /// <summary>
        /// Remove account from session
        /// </summary>
        public async Task<bool> RemoveAccountAsync(string sessionId, string userId, string? tenantId = null)
        {
            try
            {
                var session = await _sessionRepo.GetBySessionIdAsync(sessionId);
                if (session == null)
                {
                    return false;
                }

                var accountToRemove = session.Accounts.FirstOrDefault(a =>
                    string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(tenantId)
                        || string.Equals(a.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)));
                if (accountToRemove == null)
                {
                    return false;
                }

                // Remove just the requested account; delete session if that was the last account.
                bool success;
                if (session.Accounts.Count == 1)
                {
                    success = await _sessionRepo.DeleteAsync(sessionId);
                }
                else
                {
                    success = await _sessionRepo.RemoveAccountAsync(sessionId, accountToRemove.UserId, accountToRemove.TenantId);
                }

                if (success)
                {
                    _logger.LogInformation("Account removed from session: {SessionId}, user: {UserId}, tenant: {TenantId}", sessionId, userId, accountToRemove.TenantId);
                    await LogSessionEvent(SessionAuditEvents.AccountRemoved, userId, sessionId);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing account from session: {sessionId}");
                throw;
            }
        }

        /// <summary>
        /// Get all accounts in session
        /// </summary>
        public async Task<IEnumerable<IdpSessionAccount>> GetAccountsAsync(string sessionId)
        {
            try
            {
                var session = await _sessionRepo.GetBySessionIdAsync(sessionId);
                return session?.Accounts ?? new List<IdpSessionAccount>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting session accounts: {sessionId}");
                throw;
            }
        }

        /// <summary>
        /// Update session activity (refresh idle timer)
        /// </summary>
        public async Task<bool> UpdateActivityAsync(string sessionId)
        {
            try
            {
                var session = await _sessionRepo.GetBySessionIdAsync(sessionId);
                if (session == null || session.RevokedAt.HasValue)
                {
                    return false;
                }

                if (session.IsExpired())
                {
                    _logger.LogInformation("Session expired, cannot update activity: {SessionId}", sessionId);
                    return false;
                }

                return await _sessionRepo.UpdateActivityAsync(sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating session activity: {sessionId}");
                throw;
            }
        }

        /// <summary>
        /// Rotate session ID while preserving accounts and expiration windows.
        /// </summary>
        public async Task<string?> RotateSessionAsync(string sessionId, string reason)
        {
            try
            {
                var session = await _sessionRepo.GetBySessionIdAsync(sessionId);
                if (session == null || session.RevokedAt.HasValue || session.IsExpired())
                {
                    return null;
                }

                var rotated = new IdpSessionModel
                {
                    SessionId = GenerateSessionId(),
                    TenantId = session.TenantId,
                    Accounts = session.Accounts,
                    IpAddress = session.IpAddress,
                    CreatedAt = session.CreatedAt,
                    LastActivityAt = DateTime.UtcNow,
                    IdleExpiry = DateTime.UtcNow.Add(GetIdpSessionIdleTimeout()),
                    AbsoluteExpiry = session.AbsoluteExpiry
                };

                await _sessionRepo.CreateAsync(rotated);
                await _sessionRepo.DeleteAsync(sessionId);

                foreach (var account in rotated.Accounts)
                {
                    await LogSessionEvent(SessionAuditEvents.SessionRotated, account.UserId, rotated.SessionId, reason);
                }

                return rotated.SessionId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error rotating session: {sessionId}");
                throw;
            }
        }

        /// <summary>
        /// Revoke session (logout from all accounts)
        /// </summary>
        public async Task<bool> RevokeSessionAsync(string sessionId, string reason)
        {
            try
            {
                var session = await _sessionRepo.GetBySessionIdAsync(sessionId);
                if (session == null)
                {
                    return false;
                }

                // Revoke tokens associated with this session before deleting it.
                var activeSessions = await _authenticationRepository.GetActiveIdentitySessionBySessionIdAsync(sessionId);
                var refreshTokens = activeSessions
                    .Select(s => s.RefreshToken)
                    .Where(token => !string.IsNullOrWhiteSpace(token))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                foreach (var refreshToken in refreshTokens)
                {
                    await _cacheClient.RemoveKeyAsync(refreshToken);

                    var tokenModel = await _refreshTokenRepo.GetByTokenIdAsync(refreshToken);
                    if (tokenModel != null && !tokenModel.IsRevoked)
                    {
                        await _refreshTokenRepo.RevokeByTokenIdAsync(tokenModel.TokenId, $"session_revoked:{reason}");
                    }
                }

                if (refreshTokens.Count > 0)
                {
                    await _authenticationRepository.RevokeIdentitySessionsByRefreshTokensAsync(refreshTokens);
                }

                var success = await _sessionRepo.DeleteAsync(sessionId);
                if (success)
                {
                    _logger.LogInformation("Session revoked: {SessionId}, reason: {Reason}", sessionId, reason);

                    // Log for each account in session
                    foreach (var account in session.Accounts)
                    {
                        await LogSessionEvent(SessionAuditEvents.SessionRevoked, account.UserId, sessionId, reason);
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking session: {sessionId}");
                throw;
            }
        }

        /// <summary>
        /// Check if session is still active
        /// </summary>
        public async Task<bool> IsSessionActiveAsync(string sessionId)
        {
            try
            {
                var session = await _sessionRepo.GetBySessionIdAsync(sessionId);
                if (session == null || session.RevokedAt.HasValue)
                {
                    return false;
                }

                return !session.IsExpired();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking session activity: {sessionId}");
                throw;
            }
        }

        /// <summary>
        /// Get all active sessions for a user
        /// </summary>
        public async Task<IEnumerable<IdpSessionModel>> GetUserSessionsAsync(string userId, string tenantId)
        {
            try
            {
                return await _sessionRepo.GetByUserAsync(userId, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user sessions: {userId}");
                throw;
            }
        }

        // Helper methods
        private string GenerateSessionId()
        {
            byte[] buffer = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(buffer);
            return Convert.ToBase64String(buffer).Replace("/", "_").Replace("+", "-");
        }

        private async Task LogSessionEvent(string eventType, string userId, string sessionId, string details = null)
        {
            try
            {
                var auditLog = new AuditLogModel
                {
                    EventType = eventType,
                    UserId = userId,
                    Severity = AuthenticationConstants.SeverityInfo,
                    Status = AuthenticationConstants.StatusSuccess,
                    Message = details ?? eventType,
                    Timestamp = DateTime.UtcNow
                };
                await _auditLogRepo.CreateAsync(auditLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error logging session event: {eventType}");
            }
        }

        private static TimeSpan GetIdpSessionIdleTimeout()
        {
            return SessionTimeoutConfig.GetIdleTimeout();
        }

        private static TimeSpan GetIdpSessionAbsoluteTimeout()
        {
            return SessionTimeoutConfig.GetAbsoluteTimeoutDays();
        }
    }
}

