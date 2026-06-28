using Blocks.Genesis;
using Microsoft.AspNetCore.Mvc;
using CloudConfiguration.DomainService.Authentication;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public interface IConfigurationService
    {
        #region Authentication
        Task<IActionResult> GetAuthenticationConfigAsync();
        Task<BaseResponse> UpdateAuthenticationConfigAsync(UpdateAuthenticationConfigurationRequest configuration);
        #endregion
    }
}