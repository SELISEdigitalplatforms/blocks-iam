using Authentication.DomainService.Dtos;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using StackExchange.Redis;
using System.Text.Json;

namespace Authentication.DomainService.Services
{
    /// <summary>
    /// Service for managing impersonation backup tokens in Redis.
    /// Implements the dual-cache strategy where:
    /// - RefreshTokenCache = Active tokens ONLY
    /// - ImpersonationBackupCache = Dormant root token during impersonation
    /// </summary>
    public interface IImpersonationBackupService
    {
        Task<bool> BackupRootTokenAsync(string sessionId, string refreshToken, DateTime expiresUtc);
        Task<ImpersonationBackupToken?> GetBackupTokenAsync(string sessionId);
        Task<bool> UpdateBackupTokenAsync(string sessionId, string newRefreshToken, DateTime newExpiresUtc);
        Task<bool> DeleteBackupTokenAsync(string sessionId);
        string GetBackupCacheKey(string sessionId);
    }

    public class ImpersonationBackupService : IImpersonationBackupService
    {
        private const string BackupKeyPrefix = "impersonation_backup_";
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<ImpersonationBackupService> _logger;

        public ImpersonationBackupService(ICacheClient cacheClient, ILogger<ImpersonationBackupService> logger)
        {
            _cacheClient = cacheClient;
            _logger = logger;
        }

        public string GetBackupCacheKey(string sessionId) => $"{BackupKeyPrefix}{sessionId}";

        public async Task<bool> BackupRootTokenAsync(string sessionId, string refreshToken, DateTime expiresUtc)
        {
            try
            {
                var backup = new ImpersonationBackupToken
                {
                    RefreshToken = refreshToken,
                    ExpiresUtc = expiresUtc,
                    CreatedAt = DateTime.UtcNow
                };

                var key = GetBackupCacheKey(sessionId);
                var ttl = expiresUtc - DateTime.UtcNow;
                var json = JsonSerializer.Serialize(backup);

                await _cacheClient.SetStringValueAsync(key, json, ttl);
                _logger.LogInformation("Backed up root refresh token for impersonation session {SessionId}", sessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backup root token for impersonation session {SessionId}", sessionId);
                return false;
            }
        }

        public async Task<ImpersonationBackupToken?> GetBackupTokenAsync(string sessionId)
        {
            try
            {
                var key = GetBackupCacheKey(sessionId);
                var json = await _cacheClient.GetStringValueAsync(key);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<ImpersonationBackupToken>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve backup token for impersonation session {SessionId}", sessionId);
                return null;
            }
        }

        public async Task<bool> UpdateBackupTokenAsync(string sessionId, string newRefreshToken, DateTime newExpiresUtc)
        {
            try
            {
                var backup = new ImpersonationBackupToken
                {
                    RefreshToken = newRefreshToken,
                    ExpiresUtc = newExpiresUtc,
                    CreatedAt = DateTime.UtcNow,
                    LastRotated = DateTime.UtcNow
                };

                var key = GetBackupCacheKey(sessionId);
                var ttl = newExpiresUtc - DateTime.UtcNow;
                var json = JsonSerializer.Serialize(backup);

                await _cacheClient.SetStringValueAsync(key, json, ttl);
                _logger.LogInformation("Updated backup root token for impersonation session {SessionId}", sessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update backup token for impersonation session {SessionId}", sessionId);
                return false;
            }
        }

        public async Task<bool> DeleteBackupTokenAsync(string sessionId)
        {
            try
            {
                var key = GetBackupCacheKey(sessionId);
                await _cacheClient.RemoveKeyAsync(key);
                _logger.LogInformation("Deleted backup root token for impersonation session {SessionId}", sessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete backup token for impersonation session {SessionId}", sessionId);
                return false;
            }
        }
    }
}
