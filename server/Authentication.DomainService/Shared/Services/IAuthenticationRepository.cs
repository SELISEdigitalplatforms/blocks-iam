using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using MongoDB.Driver;

namespace Authentication.DomainService.Services
{
    public interface IAuthenticationRepository
    {
        IMongoCollection<T> GetCollection<T>();
        IMongoCollection<T> GetCollection<T>(string tenantId);
        IMongoCollection<T> GetCollectionByName<T>(string collectionName);
        IMongoCollection<T> GetCollectionByName<T>(string collectionName, string tenantId);
        Task<User> GetUserByEmailAsync(string email, string? tenantId = null);
        Task<User> GetUserByUsernameAsync(string username, string? organizationId = null, string? tenantId = null);
        Task<User> GetUserByIdAsync(string itemId, string? tenantId = null);
        Task<T> GetUserByIdAsync<T>(string itemId, string? tenant = null);
        Task<bool> InsertIdentitySessionAsync(IdentitySession session, string? tenant = null);
        Task<bool> InsertIdentityEventAsync(IdentityEvent identityEvent, string? tenant = null);
        Task<bool> InsertUserAuthenticationTimelineAsync(UserAuthenticationTimeline userAuthenticationTimeline, string? tenant = null);
        Task<User?> IncrementFailedLoginAndApplyLockoutAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc, string? tenantId = null);
        Task<IEnumerable<IdentitySession>> GetActiveIdentitySessionByUserIdAsync(string userId, string? tenantId = null);
        Task<IdentitySession?> GetIdentitySessionByRefreshTokenAsync(string refreshToken, string? tenantId = null);
        Task<IEnumerable<IdentitySession>> GetActiveIdentitySessionBySessionIdAsync(string sessionId, string? tenantId = null);
        Task<bool> RevokeIdentitySessionsByRefreshTokensAsync(IEnumerable<string> refreshTokens, string? tenantId = null);
        Task<bool> UpdateSessionStatusForAllRefreshTokenAsync(List<string> refreshTokens, string? tenantId = null);
        Task<bool> RevokeIdentitySessionAsync(string refreshToken, string userId, string? tenantId = null);
        Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "", string? tenantId = null);
        Task<List<IdentityProvider>> GetIdentityProvidersAsync(string? tenantId = null);
        Task<IdentityProvider?> GetIdentityProviderAsync(string provider, string? tenantId = null);
        Task<IdentityProvider?> GetIdentityProviderAsync(string provider, string providerType, string? tenantId = null);
        Task<IdentityProvider?> GetIdentityProviderByIdAsync(string id, string? tenantId = null);
        Task<IdentityProvider?> GetIdentityProviderByClientIdAsync(string clientId, string? tenantId = null);
        Task<IdentityProvider?> GetIdentityProviderByClientIdAndRedirectUriAsync(string clientId, string redirectUri, string? tenantId = null);
        Task<List<IdentityProvider>> GetIdentityProvidersByClientIdAsync(string clientId, string? tenantId = null);
        Task<IdentityProvider> CreateIdentityProviderAsync(IdentityProvider provider, string? tenantId = null);
        Task<IdentityProvider> UpdateIdentityProviderAsync(IdentityProvider provider, string? tenantId = null);
        Task DeleteIdentityProviderAsync(string id, string? tenantId = null);
        Task<AuthenticationConfiguration> GetAuthenticationConfigurationAsync(string? tenantId = null);
        Task UpdateAuthenticationConfigurationAsync(AuthenticationConfiguration authenticationConfiguration, string? tenantId = null);
        Task<OidcClientRegistration> GetOidcClientRegistrationAsync(string clientId, string? tenantId = null);
        Task<List<OidcClientRegistration>> GetOIDCCredentialsByTenantAsync(string? tenantId = null);
        Task SaveOidcClientRegistrationAsync(OidcClientRegistration credential, string? tenantId = null);
        Task<OidcClientRegistration> GetOIDCCredentialByIdAsync(string id, string? tenantId = null);
        Task DeleteOidcCliantAsync(DeleteOIDCClientRequest request, string? tenantId = null);
        Task<BiometricCredential> AuthenticateBiometricCredentialAsync(string biometricId, string biometricKey, string? tenantId = null);
        Task<ClientCredential> GetClientCredentialByIdAsync(string clientId, string? tenantId = null);
        Task<UserCode> GetUserCodeAsync(string code, string? tenantId = null);
        Task<BlocksClientConfig> GetBlocksClientAsync(string clientId, string? tenantId = null);
        Task SaveUserCodeByClientAsync(UserCode userCode, string? tenantId = null);
        Task<List<GetUserCodesByUserIdResponse>> GetUserCodesByUserIdAsync(string userId, string? tenantId = null);
        Task<BaseResponse> SaveClientCredentialAsync(ClientCredential clientCredential, string? tenantId = null);
        Task DeleteClientCredentialAsync(DeleteClientCredentialRequest request, string? tenantId = null);
        Task<List<ClientCredential>> GetClientCredentialsAsync(string? tenantId = null);
        // Impersonation session methods
        Task<bool> InsertImpersonationSessionAsync(ImpersonationSession session, string? tenantId = null);
        Task<ImpersonationSession?> GetImpersonationSessionByIdAsync(string sessionId, string? tenantId = null);
        Task<List<ImpersonationSession>> GetActiveImpersonationSessionsByUserIdAsync(string userId, string? tenantId = null);
        Task<bool> UpdateImpersonationSessionAsync(string sessionId, Dictionary<string, object> updates, string? tenantId = null);
        Task<bool> RevokeIdentitySessionsByUserIdAsync(string userId, string? tenantId = null);
        Task<bool> RevokeIdentitySessionsBySessionIdsAsync(IEnumerable<string> sessionIds, string? tenantId = null);
    }
}
