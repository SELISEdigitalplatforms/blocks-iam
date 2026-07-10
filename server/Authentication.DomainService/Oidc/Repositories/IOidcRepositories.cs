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
    /// Manages refresh tokens
    /// </summary>
    public interface IRefreshTokenRepository
    {
        Task<string> CreateAsync(RefreshTokenModel token);
        Task<RefreshTokenModel> GetByTokenIdAsync(string tokenId);
        Task<IEnumerable<RefreshTokenModel>> GetBySessionIdAsync(string sessionId);
        Task<IEnumerable<RefreshTokenModel>> GetRotationHistoryAsync(string sessionId);
        Task<IEnumerable<RefreshTokenModel>> GetByUserAsync(string userId, string tenantId);
        Task<bool> RevokeByTokenIdAsync(string tokenId, string reason);
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
}

