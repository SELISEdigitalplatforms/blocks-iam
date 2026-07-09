using Authentication.DomainService.Shared;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Facade for the refresh-token flow: cache storage, refresh-token service, OAuth JWT issuer, tenant lookup.
    /// </summary>
    public interface ITokenRefresher
    {
        Task<string> GetCacheValueAsync(string key);
        Task RemoveKeyAsync(string key);
        Task<Tenant?> GetTenantByIDAsync(string tenantId);
        Task<TokenResponse> AuthenticateAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration, User user);
    }
}
