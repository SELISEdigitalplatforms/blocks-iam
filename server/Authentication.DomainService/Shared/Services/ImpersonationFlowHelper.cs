using Authentication.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;

namespace Authentication.DomainService.Services
{
    /// <summary>
    /// Helper methods for impersonation session management.
    /// Handles session creation, token rotation, invalidation, and restoration.
    /// </summary>
    public static class ImpersonationFlowHelper
    {
        /// <summary>
        /// Creates an impersonation session and backs up the root refresh token to Redis.
        /// </summary>
        public static async Task<string> CreateAndBackupImpersonationSessionAsync(
            string userId,
            string targetTenantId,
            string? organizationId,
            string rootRefreshToken,
            DateTime rootTokenExpiresUtc,
            IAuthenticationRepository repository,
            IImpersonationBackupService backupService)
        {
            var sessionId = Guid.NewGuid().ToString();

            // Create impersonation session record
            var impersonationSession = new ImpersonationSession
            {
                Id = sessionId,
                UserId = userId,
                TargetTenantId = targetTenantId,
                OrganizationId = organizationId ?? "default",
                StartedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                Status = "active"
            };

            var dbInsertSuccess = await repository.InsertImpersonationSessionAsync(impersonationSession);
            if (!dbInsertSuccess)
            {
                throw new InvalidOperationException("Failed to create impersonation session in database");
            }

            // Backup root refresh token to Redis (for root session restore)
            var backupSuccess = await backupService.BackupRootTokenAsync(sessionId, rootRefreshToken, rootTokenExpiresUtc);
            if (!backupSuccess)
            {
                throw new InvalidOperationException("Failed to backup root token to Redis");
            }

            return sessionId;
        }

        /// <summary>
        /// Switches organization context within existing impersonation session.
        /// Used when user wants to change org while impersonating same tenant.
        /// </summary>
        public static async Task<bool> SwitchOrganizationContextAsync(
            string impersonationSessionId,
            string newOrganizationId,
            IAuthenticationRepository repository)
        {
            var session = await repository.GetImpersonationSessionByIdAsync(impersonationSessionId);
            if (session == null || session.Status != "active")
            {
                return false;
            }

            var updates = new Dictionary<string, object>
            {
                { "org_id", newOrganizationId ?? "default" },
                { "last_activity", DateTime.UtcNow }
            };

            return await repository.UpdateImpersonationSessionAsync(impersonationSessionId, updates);
        }

        /// <summary>
        /// Rotates the backup root refresh token using OAuth and updates the backup cache.
        /// Checks expiration with a grace period before attempting rotation.
        /// </summary>
        public static async Task<(bool Success, string? NewRootRefreshToken, DateTime? NewExpiresUtc, string? ErrorCode)> RotateBackupRootTokenAsync(
            string impersonationSessionId,
            IImpersonationBackupService backupService,
            Func<string, Task<(string AccessToken, string RefreshToken, DateTime ExpiresUtc)>> oAuthRotateFunc,
            AuthenticationConfiguration config,
            ILogger logger)
        {
            try
            {
                // Retrieve backup from Redis
                var backup = await backupService.GetBackupTokenAsync(impersonationSessionId);
                if (backup == null)
                {
                    logger.LogWarning("Backup token not found for impersonation session {SessionId}", impersonationSessionId);
                    return (false, null, null, "backup_not_found");
                }

                // Check if backup is about to expire
                var gracePeriod = TimeSpan.FromMinutes(config.TokenRotationGracePeriodMinutes);
                if (backup.ExpiresUtc <= DateTime.UtcNow.Add(gracePeriod))
                {
                    logger.LogWarning("Backup token expired or near expiry for impersonation session {SessionId}. ExpiresUtc: {ExpiresUtc}", impersonationSessionId, backup.ExpiresUtc);
                    await backupService.DeleteBackupTokenAsync(impersonationSessionId);
                    return (false, null, null, "backup_expired");
                }

                // Call OAuth endpoint to refresh root token (with explicit error handling)
                string newAccessToken, newRefreshToken;
                DateTime newExpiresUtc;
                try
                {
                    (newAccessToken, newRefreshToken, newExpiresUtc) = await oAuthRotateFunc(backup.RefreshToken);
                }
                catch (HttpRequestException ex)
                {
                    logger.LogError(ex, "Network error during root token rotation for impersonation session {SessionId}. OAuth endpoint unreachable.", impersonationSessionId);
                    return (false, null, null, "network_error");
                }
                catch (TaskCanceledException ex)
                {
                    logger.LogError(ex, "Timeout during root token rotation for impersonation session {SessionId}", impersonationSessionId);
                    return (false, null, null, "timeout_error");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error during root token rotation for impersonation session {SessionId}", impersonationSessionId);
                    return (false, null, null, "internal_error");
                }

                // Validate OAuth response
                if (string.IsNullOrWhiteSpace(newRefreshToken))
                {
                    logger.LogError("OAuth endpoint returned empty refresh token for impersonation session {SessionId}", impersonationSessionId);
                    return (false, null, null, "invalid_token_response");
                }

                if (newExpiresUtc <= DateTime.UtcNow)
                {
                    logger.LogError("OAuth endpoint returned expired token for impersonation session {SessionId}. ExpiresUtc: {ExpiresUtc}", impersonationSessionId, newExpiresUtc);
                    return (false, null, null, "token_already_expired");
                }

                // Update backup in Redis with new token
                try
                {
                    var updateSuccess = await backupService.UpdateBackupTokenAsync(
                        impersonationSessionId,
                        newRefreshToken,
                        newExpiresUtc);

                    if (!updateSuccess)
                    {
                        logger.LogError("Failed to update backup token in Redis for impersonation session {SessionId}", impersonationSessionId);
                        return (false, null, null, "cache_update_failed");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Exception while updating backup token in Redis for impersonation session {SessionId}", impersonationSessionId);
                    return (false, null, null, "cache_error");
                }

                logger.LogInformation("Successfully rotated root token for impersonation session {SessionId}. New expiry: {ExpiresUtc}", impersonationSessionId, newExpiresUtc);
                return (true, newRefreshToken, newExpiresUtc, null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error during root token rotation for impersonation session {SessionId}", impersonationSessionId);
                return (false, null, null, "unknown_error");
            }
        }

        /// <summary>
        /// Invalidates the root session and removes the backup token from Redis.
        /// </summary>
        public static async Task<bool> InvalidateRootSessionAndBackupAsync(
            string rootSessionId,
            string impersonationSessionId,
            IAuthenticationRepository repository,
            IImpersonationBackupService backupService,
            ILogger logger)
        {
            try
            {
                // Mark root session as revoked
                var rootSessionUpdates = new Dictionary<string, object>
                {
                    { "is_revoked", true },
                    { "revoked_at", DateTime.UtcNow },
                    { "revocation_reason", "logout_during_impersonation" }
                };

                await repository.UpdatePartialAsync<IdentitySession>(rootSessionId, rootSessionUpdates);

                // Delete backup from Redis (prevents accidental reuse)
                var deleteSuccess = await backupService.DeleteBackupTokenAsync(impersonationSessionId);
                if (!deleteSuccess)
                {
                    logger.LogWarning("Failed to delete backup token for impersonation session {SessionId}", impersonationSessionId);
                }

                // Mark impersonation session as ended
                var impersonationUpdates = new Dictionary<string, object>
                {
                    { "status", "ended_by_logout" },
                    { "ended_at", DateTime.UtcNow },
                    { "reason", "logout_during_impersonation" }
                };

                await repository.UpdateImpersonationSessionAsync(impersonationSessionId, impersonationUpdates);

                logger.LogInformation("Invalidated root session and backup for impersonation {ImpersonationSessionId}", impersonationSessionId);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error invalidating root session for impersonation session {SessionId}", impersonationSessionId);
                return false;
            }
        }

        /// <summary>
        /// Restores the root session by rotating the backup token and cleaning up impersonation resources.
        /// </summary>
        public static async Task<bool> RestoreRootSessionAsync(
            string impersonationSessionId,
            string userId,
            IImpersonationBackupService backupService,
            IAuthenticationRepository repository,
            Func<string, Task<(string AccessToken, string RefreshToken, DateTime ExpiresUtc)>> oAuthRotateFunc,
            ILogger logger)
        {
            try
            {
                // Retrieve backup from Redis
                var backup = await backupService.GetBackupTokenAsync(impersonationSessionId);
                if (backup == null || backup.ExpiresUtc <= DateTime.UtcNow)
                {
                    logger.LogError("Root backup token missing or expired for impersonation session {SessionId}", impersonationSessionId);
                    return false;
                }

                // Rotate root token via OAuth
                var (_, newRootRefreshToken, newExpiresUtc) = await oAuthRotateFunc(backup.RefreshToken);

                // Delete backup from Redis after successful rotation
                var deleteSuccess = await backupService.DeleteBackupTokenAsync(impersonationSessionId);
                if (!deleteSuccess)
                {
                    logger.LogWarning("Failed to delete backup token after restore for impersonation session {SessionId}", impersonationSessionId);
                }

                // Mark impersonation session as ended
                var impersonationUpdates = new Dictionary<string, object>
                {
                    { "status", "ended_by_admin_stop" },
                    { "ended_at", DateTime.UtcNow }
                };

                await repository.UpdateImpersonationSessionAsync(impersonationSessionId, impersonationUpdates);

                logger.LogInformation("Successfully restored root session and ended impersonation {ImpersonationSessionId}", impersonationSessionId);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error restoring root session for impersonation session {SessionId}", impersonationSessionId);
                return false;
            }
        }

        /// <summary>
        /// Reads the impersonation session ID from cookies.
        /// </summary>
        public static bool TryGetImpersonationSessionId(HttpRequest httpRequest, out string? sessionId)
        {
            sessionId = null;
            if (httpRequest.Cookies.TryGetValue("impersonation_session_id", out var value) && !string.IsNullOrWhiteSpace(value))
            {
                sessionId = value;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Extracts the expiration time from a JWT token.
        /// </summary>
        public static DateTime? GetJwtExpiryUtc(string token)
        {
            try
            {
                var jwtHandler = new JwtSecurityTokenHandler();
                if (jwtHandler.CanReadToken(token))
                {
                    var jwtToken = jwtHandler.ReadJwtToken(token);
                    return jwtToken.ValidTo;
                }
            }
            catch
            {
                // Log if needed
            }
            return null;
        }

        /// <summary>
        /// Checks if an impersonation session is currently active.
        /// </summary>
        public static bool IsInImpersonationMode(HttpRequest httpRequest)
        {
            return TryGetImpersonationSessionId(httpRequest, out _);
        }
    }
}
