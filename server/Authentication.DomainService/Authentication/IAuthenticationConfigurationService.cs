using Blocks.Genesis;
using Microsoft.AspNetCore.Mvc;
using Authentication.DomainService.Authentication.RequestModel;

namespace Authentication.DomainService.Authentication
{
    public interface IAuthenticationConfigurationService
    {
        Task<IActionResult> GetAuthenticationConfigAsync();
        Task<BaseResponse> UpdateAuthenticationConfigAsync(UpdateAuthenticationConfigurationRequest configuration);
    }
}