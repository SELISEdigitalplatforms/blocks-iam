using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Blocks.Genesis;
using Iam.DomainService.Entities;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

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
        Task<User?> IncrementFailedLoginAndApplyLockoutAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc);

        /// <summary>
        /// Atomically reserves one password check BEFORE the password is verified: counts it as a failed
        /// attempt up front. Refused (null) while the account is locked or when the lock threshold is already
        /// taken by earlier or in-flight attempts, so parallel requests cannot run more checks than the threshold.
        /// Once the threshold is reached, one attempt per <see cref="AuthenticationRepository.LoginAttemptLease"/>
        /// is allowed again (after a lock expires, or when a reserved attempt never finished).
        /// </summary>
        Task<User?> TryReserveLoginAttemptAsync(string userId, int lockThreshold, DateTime nowUtc);

        /// <summary>
        /// Gives back a reservation whose password was correct. False when the account is locked now (locked by
        /// concurrent failures, or the user is gone): the caller must not sign the user in.
        /// </summary>
        Task<bool> ReleaseLoginAttemptAsync(string userId, DateTime nowUtc);

        /// <summary>
        /// Locks the account after a wrong password whose reservation reached the threshold (the attempt is already
        /// counted). Same exponential backoff as <see cref="IncrementFailedLoginAndApplyLockoutAsync"/>. Returns the
        /// locked user when this call applied the lock, otherwise null.
        /// </summary>
        Task<User?> LockAccountIfLoginThresholdReachedAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc);
        Task<User?> IncrementFailedMfaAndApplyLockoutAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc);
        Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "");
        Task<List<IdentityProvider>> GetIdentityProvidersAsync();
        /// <summary>As the caller may see it: properties a View column rule hides are cleared. Never save it back.</summary>
        Task<List<IdentityProvider>> GetIdentityProvidersForViewAsync();
        Task<IdentityProvider?> GetIdentityProviderAsync(string provider);
        Task<IdentityProvider?> GetIdentityProviderAsync(string provider, string providerType);
        Task<IdentityProvider?> GetIdentityProviderByIdAsync(string id);
        /// <summary>As the caller may see it: properties a View column rule hides are cleared. Never save it back.</summary>
        Task<IdentityProvider?> GetIdentityProviderByIdForViewAsync(string id);
        Task<IdentityProvider?> GetIdentityProviderByClientIdAsync(string clientId);
        Task<IdentityProvider?> GetIdentityProviderByClientIdAndRedirectUriAsync(string clientId, string redirectUri);
        Task<IdentityProvider> CreateIdentityProviderAsync(IdentityProvider provider);
        Task<IdentityProvider> UpdateIdentityProviderAsync(IdentityProvider provider);
        Task DeleteIdentityProviderAsync(string id);
        Task<IdentityConfiguration> GetAuthenticationConfigurationAsync();
        /// <summary>As the caller may see it: properties a View column rule hides are cleared. Never save it back.</summary>
        Task<IdentityConfiguration> GetAuthenticationConfigurationForViewAsync();
        Task UpdateAuthenticationConfigurationAsync(IdentityConfiguration authenticationConfiguration);
        Task<OidcClientRegistration> GetOidcClientRegistrationAsync(string clientId);
        Task<List<OidcClientRegistration>> GetOIDCCredentialsByTenantAsync();
        /// <summary>As the caller may see it: properties a View column rule hides are cleared. Never save it back.</summary>
        Task<List<OidcClientRegistration>> GetOIDCCredentialsByTenantForViewAsync();
        Task SaveOidcClientRegistrationAsync(OidcClientRegistration credential);
        Task<OidcUiTemplate?> GetOidcUiTemplateAsync();
        Task SaveOidcUiTemplateAsync(OidcUiTemplate template);
        Task<OidcClientRegistration> GetOIDCCredentialByIdAsync(string id);
        Task DeleteOidcClientAsync(DeleteOIDCClientRequest request);
        Task<BiometricCredential> AuthenticateBiometricCredentialAsync(string biometricId, string biometricKey);
        /// <remarks>
        /// Deliberately unscoped: the client_credentials grant reaches this with a client id and
        /// secret and no caller context at all, so an organization clause here would break token
        /// issuance for every credential outside the default organization. Callers that act on
        /// behalf of an administrator must check <c>OrganizationAccessScope.Allows</c> themselves.
        /// </remarks>
        Task<ClientCredential> GetClientCredentialByIdAsync(string clientId);
        Task<BlocksClientConfig> GetBlocksClientAsync(string clientId);
        Task<BaseResponse> SaveClientCredentialAsync(ClientCredential clientCredential);
        Task DeleteClientCredentialAsync(DeleteClientCredentialRequest request);
        Task<List<ClientCredential>> GetClientCredentialsAsync(string? organizationId);
        // Impersonation session methods
        Task<bool> InsertImpersonationSessionAsync(ImpersonationSession session);
        Task<ImpersonationSession?> GetImpersonationSessionByIdAsync(string sessionId);
        Task<List<ImpersonationSession>> GetActiveImpersonationSessionsByUserIdAsync(string userId);
        Task<bool> UpdateImpersonationSessionAsync(string sessionId, Dictionary<string, object> updates);
    }
}
