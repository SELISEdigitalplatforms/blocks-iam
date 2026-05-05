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
        Task<SaveSsoCredentialResponse> SaveSocialLoginCredentialAsync(SaveSsoCredentialRequest credential);
        Task<BaseResponse> DeleteSocialLoginCredentialAsync(string itemId);
        Task<GetSsoCredentialResponse> GetSsoCredentialAsync(string itemId);
        Task<List<SocialLoginCredential>> GetSocialLoginCredentialsAsync();
        Task<BaseResponse> UpdateSsoCredentialStatusAsync(UpdateSsoCredentialStatusRequest request);
        Task<SaveOIDCClientResponse> SaveOIDCClientAsync(SaveOIDCClientRequest request);
        Task<BaseResponse> DeleteOIDCClientAsyncAsync(DeleteOIDCClientRequest request);
        Task<BaseResponse> GenerateUserCodeByClientAsync(GenerateUserCodeRequest request);
        Task<GetOIDCClientResponse> GetOIDCClientAsyncAsync(string tenantId);
        Task<GetOIDCClientsResponse> GetOIDCClientsAsyncAsync();
        Task<BaseResponse> SaveClientCredentialAsync(SaveClientCredentialRequest request);
        Task<BaseResponse> DeleteClientCredentialAsync(DeleteClientCredentialRequest request);
        Task<List<ClientCredential>> GetClientCredentialsAsync(GetAllClientCredentialsRequest request);
    }
}
