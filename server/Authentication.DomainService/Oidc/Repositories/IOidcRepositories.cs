using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Idp.DomainService.Oidc.Contracts;

namespace Authentication.DomainService.Oidc.Repositories
{
    /// <summary>
    /// Authorization Code Repository
    /// Manages one-time authorization codes with PKCE challenge storage
    /// </summary>
    public interface IAuthorizationCodeRepository
    {
        Task<string> CreateAsync(AuthorizationCodeModel code);
        Task<AuthorizationCodeModel> GetByCodeAsync(string code);
        Task<bool> DeleteAsync(string code);
        Task<IEnumerable<AuthorizationCodeModel>> GetExpiredAsync();
    }

    /// <summary>
    /// Refresh Token Repository
    /// Manages refresh tokens. Source of truth for cross-app session state (TokenId == _id).
    /// </summary>
    public interface IRefreshTokenRepository
    {
        Task<string> CreateAsync(RefreshTokenModel token);
        Task<RefreshTokenModel> GetByTokenIdAsync(string tokenId);
        Task<IReadOnlyList<RefreshTokenModel>> GetBySessionIdAsync(string sessionId);
        Task<IReadOnlyList<RefreshTokenModel>> GetActiveTokensBySessionIdAsync(string sessionId);
        Task<IReadOnlyList<RefreshTokenModel>> GetActiveTokensByUserAsync(string userId);
        Task<IEnumerable<RefreshTokenModel>> GetRotationHistoryAsync(string sessionId);
        Task<IEnumerable<RefreshTokenModel>> GetByUserAsync(string userId, string tenantId);
        Task<bool> RevokeByTokenIdAsync(string tokenId, string reason);
        Task<int> RevokeAllByTokenIdsAsync(IEnumerable<string> tokenIds, string reason);
        Task<int> RevokeAllBySessionIdAsync(string sessionId, string reason);
        Task<bool> UpdateSlidingExpiryAsync(string tokenId);
        Task<bool> DeleteAsync(string tokenId);
        Task<IEnumerable<RefreshTokenModel>> GetExpiredAsync();
    }

    /// <summary>
    /// IdP Session Repository
    /// Manages IdP sessions for multi-account SSO support
    /// </summary>
    public interface IIdpSessionRepository
    {
        Task<string> CreateAsync(IdpSessionModel session);
        Task<IdpSessionModel> GetBySessionIdAsync(string sessionId);
        Task<bool> AddAccountAsync(string sessionId, IdpSessionAccount account);
        Task<bool> RemoveAccountAsync(string sessionId, string userId, string tenantId);
        Task<bool> UpdateActivityAsync(string sessionId);
        Task<bool> RevokeAsync(string sessionId);
        Task<bool> DeleteAsync(string sessionId);
        Task<IEnumerable<IdpSessionModel>> GetByUserAsync(string userId, string tenantId);
    }

    /// <summary>
    /// Token Revocation Repository
    /// Maintains JTI blacklist for immediate token revocation
    /// </summary>
    public interface ITokenRevocationRepository
    {
        Task<bool> RevokeTokenAsync(string jti, string userId, string reason, DateTime expiresAt);
        Task<bool> IsRevokedAsync(string jti);
        Task<TokenRevocationModel> GetRevocationDetailsAsync(string jti);
        Task<IEnumerable<TokenRevocationModel>> GetRevokedTokensByUserAsync(string userId);
        Task<IEnumerable<TokenRevocationModel>> GetByUserAsync(string userId);
        Task<bool> DeleteAsync(string jti);
    }

    /// <summary>
    /// Consent Grant Repository
    /// Manages user pre-approved scopes for clients
    /// </summary>
    public interface IConsentGrantRepository
    {
        Task<string> CreateAsync(ConsentGrantModel consent);
        Task<ConsentGrantModel> GetAsync(string userId, string clientId, string tenantId);
        Task<bool> UpdateAsync(ConsentGrantModel consent);
        Task<bool> DeleteAsync(string userId, string clientId, string tenantId);
        Task<IEnumerable<ConsentGrantModel>> GetByUserAsync(string userId, string tenantId);
    }

    /// <summary>
    /// RFC 8628 Device Authorization Grant repository. Persists pending device/user code pairs
    /// in MongoDB. The <c>device_code</c> is stored as a SHA-256 hex digest (raw value is never
    /// written). Atomicity of the <c>Approved → Consumed</c> transition is enforced by a
    /// conditional update — RFC 8628 §6.5 acceptance #17.
    /// </summary>
    public interface IDeviceAuthorizationRepository
    {
        Task CreateAsync(DeviceAuthorizationRequestModel entity, CancellationToken ct = default);
        Task<DeviceAuthorizationRequestModel?> GetByDeviceCodeHashAsync(string hash, CancellationToken ct = default);
        Task<DeviceAuthorizationRequestModel?> GetByUserCodeAsync(string userCode, CancellationToken ct = default);
        Task<DeviceAuthorizationRequestModel?> GetByIdAsync(string id, CancellationToken ct = default);
        Task<bool> MarkApprovedAsync(string id, string userId, DateTime at, CancellationToken ct = default);
        Task<bool> MarkDeniedAsync(string id, DateTime at, CancellationToken ct = default);
        Task<bool> MarkConsumedAsync(string id, DateTime at, CancellationToken ct = default);
        Task<bool> MarkExpiredAsync(IEnumerable<string> ids, CancellationToken ct = default);
        Task<bool> UpdatePollAsync(string id, DateTime lastPollAt, int pollsObserved, CancellationToken ct = default);
        Task<int> BumpPollIntervalAsync(string id, int currentInterval, CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetExpiredIdsAsync(DateTime olderThanUtc, int limit, CancellationToken ct = default);
        Task EnsureIndexesAsync(CancellationToken ct = default);
    }
}

