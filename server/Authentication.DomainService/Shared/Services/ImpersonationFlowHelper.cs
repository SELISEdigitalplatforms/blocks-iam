using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using System.Text.Json;


namespace Authentication.DomainService.Shared.Services
{
    public class ImpersonationBackupToken
    {
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresUtc { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public interface IImpersonationFlowHelper
    {
        Task<string> CreateAndBackupImpersonationSessionAsync(
            string userId,
            string rootTenantId,
            string targetTenantId,
            string? organizationId,
            string rootRefreshToken,
            DateTime rootTokenExpiresUtc);
        Task<bool> SwitchOrganizationContextAsync(string impersonationSessionId, string newOrganizationId);
        Task<ImpersonationBackupToken?> GetBackupTokenAsync(string sessionId);
        Task<bool> DeleteBackupTokenAsync(string sessionId);

    }
    
    public class ImpersonationFlowHelper : IImpersonationFlowHelper
    {
        private readonly IAuthenticationRepository _repository;
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<ImpersonationFlowHelper> _logger;
        private const string BackupKeyPrefix = "impersonation_backup_";

        public ImpersonationFlowHelper(
            IAuthenticationRepository repository,
            ICacheClient cacheClient, ILogger<ImpersonationFlowHelper> logger
        ) 
        { 
            _repository = repository;
            _cacheClient = cacheClient;
            _logger = logger;
        }

        public async Task<bool> SwitchOrganizationContextAsync(
            string impersonationSessionId,
            string newOrganizationId)
        {
            var bc = BlocksContext.GetContext();
            var session = await _repository.GetImpersonationSessionByIdAsync(impersonationSessionId);
            if (session == null || session.Status != "active")
            {
                return false;
            }

            var updates = new Dictionary<string, object>
            {
                { "OrganizationId", newOrganizationId ?? "default" },
                { "LastActivity", DateTime.UtcNow }
            };

            return await _repository.UpdateImpersonationSessionAsync(impersonationSessionId, updates);
        }

        public async Task<string> CreateAndBackupImpersonationSessionAsync(
            string userId,
            string rootTenantId,
            string targetTenantId,
            string? organizationId,
            string rootRefreshToken,
            DateTime rootTokenExpiresUtc)
        {
            var sessionId = Guid.NewGuid().ToString();

            // Create impersonation session record
            var impersonationSession = new ImpersonationSession
            {
                Id = sessionId,
                UserId = userId,
                TargetTenantId = targetTenantId,
                RootTenantId = rootTenantId,
                OrganizationId = organizationId ?? "default",
                StartedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                Status = "active"
            };
            var bc = BlocksContext.GetContext();

            var dbInsertSuccess = await _repository.InsertImpersonationSessionAsync(impersonationSession);
            if (!dbInsertSuccess)
            {
                throw new InvalidOperationException("Failed to create impersonation session in database");
            }

            // Backup root refresh token to Redis (for root session restore)
            var backupSuccess = await BackupRootTokenAsync(sessionId, rootRefreshToken, rootTokenExpiresUtc);
            if (!backupSuccess)
            {
                throw new InvalidOperationException("Failed to backup root token to Redis");
            }

            return sessionId;
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

                await _cacheClient.AddStringValueAsync(key, json, (long)ttl.TotalSeconds);
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
