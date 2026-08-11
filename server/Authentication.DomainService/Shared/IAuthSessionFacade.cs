using Authentication.DomainService.Authentication;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Shared.Services;
using Iam.DomainService.Entities;
using Idp.DomainService.Oidc.Contracts;

namespace Authentication.DomainService.Shared
{
    /// <summary>
    /// Facade bundling the session-related services that <c>AuthenticationService</c>
    /// orchestrates: IdP sessions, impersonation, token revocation, unified refresh-token
    /// sessions, and OAuth JWT access-token issuance. Reduces DI count from 5 to 1 (S107).
    /// </summary>
    public interface IAuthSessionFacade
    {
        // IdP session lifecycle
        Task<string> CreateSessionAsync(string userId, string tenantId, string ipAddress);
        Task<IdpSessionModel> GetSessionAsync(string sessionId);
        Task<bool> AddAccountAsync(string sessionId, string userId, string tenantId, string displayName);
        Task<bool> UpdateActivityAsync(string sessionId);
        Task<bool> RemoveAccountAsync(string sessionId, string userId, string? tenantId = null);
        Task<string?> RotateSessionAsync(string sessionId, string reason);
        Task<bool> RevokeSessionAsync(string sessionId, string reason);

        // Impersonation
        Task<string> CreateAndBackupImpersonationSessionAsync(string userId, string rootTenantId, string targetTenantId, string clientId, string organizationId);
        Task<bool> SwitchOrganizationContextAsync(string sessionId, string organizationId);

        // Token revocation + refresh
        Task<TokenRevocationResult> RevokeTokenAsync(string token, string grantType, string? clientId);
        Task RevokeRefreshToken(string refreshToken);

        // Token issuance
        Task<TokenResponse> ManageTokenAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration, User? user);

        /// <summary>
        /// The shared refresh-token validity check. Returns null when the presented token cannot be used;
        /// a result carrying a different token id is a grace-window replay onto the rotation successor.
        /// </summary>
        Task<RefreshTokenCache?> TryResolveRefreshSessionAsync(string presentedTokenId, IdentityConfiguration configuration);
    }
}
