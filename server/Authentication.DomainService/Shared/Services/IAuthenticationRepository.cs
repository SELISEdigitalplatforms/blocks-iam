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
        Task<User> GetUserByEmailAsync(string email);
        Task<User> GetUserByUsernameAsync(string username, string? organizationId = null);
        Task<User> GetUserByIdAsync(string itemId);
        Task<T> GetUserByIdAsync<T>(string itemId);
        Task<bool> InsertIdentitySessionAsync(IdentitySession session, string? tenant = null);
        Task<bool> InsertIdentityEventAsync(IdentityEvent identityEvent, string? tenant = null);
        Task<bool> InsertUserAuthenticationTimelineAsync(UserAuthenticationTimeline userAuthenticationTimeline, string? tenant = null);
        Task<User?> IncrementFailedLoginAndApplyLockoutAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc);
        Task<IEnumerable<IdentitySession>> GetActiveIdentitySessionByUserIdAsync(string userId);
        Task<IdentitySession?> GetIdentitySessionByRefreshTokenAsync(string refreshToken);
        Task<IEnumerable<IdentitySession>> GetActiveIdentitySessionBySessionIdAsync(string sessionId);
        Task<bool> RevokeIdentitySessionsByRefreshTokensAsync(IEnumerable<string> refreshTokens);
        Task<bool> UpdateSessionStatusForAllRefreshTokenAsync(List<string> refreshTokens);
        Task<bool> RevokeIdentitySessionAsync(string refreshToken, string userId);
        Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "");
        Task<List<IdentityProvider>> GetIdentityProvidersAsync();
        Task<IdentityProvider?> GetIdentityProviderAsync(string provider);
        Task<IdentityProvider?> GetIdentityProviderAsync(string provider, string providerType);
        Task<IdentityProvider?> GetIdentityProviderByIdAsync(string id);
        Task<IdentityProvider?> GetIdentityProviderByClientIdAsync(string clientId);
        Task<IdentityProvider?> GetIdentityProviderByClientIdAndRedirectUriAsync(string clientId, string redirectUri);
        Task<List<IdentityProvider>> GetIdentityProvidersByClientIdAsync(string clientId);
        Task<IdentityProvider> CreateIdentityProviderAsync(IdentityProvider provider);
        Task<IdentityProvider> UpdateIdentityProviderAsync(IdentityProvider provider);
        Task DeleteIdentityProviderAsync(string id);
        Task<AuthenticationConfiguration> GetAuthenticationConfigurationAsync();
        Task UpdateAuthenticationConfigurationAsync(AuthenticationConfiguration authenticationConfiguration);
        Task<OidcClientRegistration> GetOidcClientRegistrationAsync(string clientId);
        Task<List<OidcClientRegistration>> GetOIDCCredentialsByTenantAsync();
        Task SaveOidcClientRegistrationAsync(OidcClientRegistration credential);
        Task<OidcClientRegistration> GetOIDCCredentialByIdAsync(string id);
        Task DeleteOidcClientAsync(DeleteOIDCClientRequest request);
        Task<BiometricCredential> AuthenticateBiometricCredentialAsync(string biometricId, string biometricKey);
        Task<ClientCredential> GetClientCredentialByIdAsync(string clientId);
        Task<UserCode> GetUserCodeAsync(string code);
        Task<BlocksClientConfig> GetBlocksClientAsync(string clientId);
        Task SaveUserCodeByClientAsync(UserCode userCode);
        Task<List<GetUserCodesByUserIdResponse>> GetUserCodesByUserIdAsync(string userId);
        Task<BaseResponse> SaveClientCredentialAsync(ClientCredential clientCredential);
        Task DeleteClientCredentialAsync(DeleteClientCredentialRequest request);
        Task<List<ClientCredential>> GetClientCredentialsAsync();
        // Impersonation session methods
        Task<bool> InsertImpersonationSessionAsync(ImpersonationSession session);
        Task<ImpersonationSession?> GetImpersonationSessionByIdAsync(string sessionId);
        Task<List<ImpersonationSession>> GetActiveImpersonationSessionsByUserIdAsync(string userId);
        Task<bool> UpdateImpersonationSessionAsync(string sessionId, Dictionary<string, object> updates);
        Task<bool> RevokeIdentitySessionsByUserIdAsync(string userId);
        Task<bool> RevokeIdentitySessionsBySessionIdsAsync(IEnumerable<string> sessionIds);
    }
}
