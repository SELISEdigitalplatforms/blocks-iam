using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Shared;
using Authentication.DomainService.ResponseModel;
using Microsoft.AspNetCore.Http;
using Authentication.DomainService.Shared.ResponseModel;
using Authentication.DomainService.Shared.RequestModel;
using Iam.DomainService.Dtos;

namespace Authentication.DomainService.Services
{
    public interface IAuthenticationDomainService
    {
        IEnumerable<string> GetVisitorsIpAddresses(HttpContext context);
        string GetRequestOriginHostName(HttpContext context);
        Task SendToQueueAsync<T>(string queue, T payload) where T : class;
        DeviceInformation? GetDeviceInfo(string userAgent);
        Task<SaveOIDCClientResponse> SaveOIDCClientAsync(SaveOIDCClientRequest request);
        Task<OidcUiTemplate?> GetOidcTemplateForManagementAsync();
        Task<SaveOidcUiTemplateResponse> SaveOidcUiTemplateRequestAsync(SaveOidcUiTemplateRequest request);
        Task<BaseResponse> DeleteOidcClientAsync(DeleteOIDCClientRequest request);
        Task<BaseResponse> GenerateUserCodeByClientAsync(GenerateUserCodeRequest request);
        Task<GetOIDCClientResponse> GetOidcClientAsync(string tenantId);
        Task<GetOIDCClientsResponse> GetOidcClientsAsync();
        Task<BaseResponse> SaveClientCredentialAsync(SaveClientCredentialRequest request);
        Task<BaseResponse> DeleteClientCredentialAsync(DeleteClientCredentialRequest request);
        Task<List<ClientCredential>> GetClientCredentialsAsync(GetAllClientCredentialsRequest request);
        Task<BaseResponse> CreateIdentityProviderAsync(SaveIdentityProviderRequest request);
        Task<IdentityProvider?> GetIdentityProviderAsync(string provider);
        Task<IdentityProvider?> GetIdentityProviderByIdAsync(string id);
        Task<List<IdentityProvider>> GetAllIdentityProvidersAsync();
        Task<BaseResponse> UpdateIdentityProviderAsync(string id, UpdateIdentityProviderRequest request);
        Task<BaseResponse> DeleteIdentityProviderAsync(string id);
        Task<BaseResponse> UpdateIdentityProviderStatusAsync(string id, bool isActive);
        Task<RotateOidcClientSecretResponse> RotateOidcClientSecretAsync(string itemId);
    }
}
