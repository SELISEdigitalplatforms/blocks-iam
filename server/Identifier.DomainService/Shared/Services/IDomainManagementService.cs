using Blocks.Genesis;
using Identifier.DomainService.Shared.Entities;

namespace Identifier.DomainService.Shared
{
    public interface IDomainManagementService
    {
        Task<BaseResponse> ConfigureDomainAsync(ConfigureDomainRequest request);
        Task<(bool, string)> DisableDomainBindingAsync(DisableDomainBindingRequest request);
    }
}
