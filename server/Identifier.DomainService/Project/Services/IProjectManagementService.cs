using Blocks.Genesis;
using Identifier.DomainService.Dtos;
using Identifier.DomainService.Entities;
using Identifier.DomainService.Shared;
using Identifier.DomainService.Shared.Entities;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Identifier.DomainService.Projects
{
    public interface IProjectManagementService
    {
        Task<List<GroupedProjectsDto>> GetAllAsync(GetProjectsRequest request);
        Task<GetProjectResponse> GetAsync(string projectId);
        Task<GetAssetResponse> GetAssetAsync(GetAssetRequest request);
        Task<IActionResult> GetProjectTokenValidationParametersAsync(string projectId);
        Task<ThirdPartyJWTClaims?> GetThirdPartyJWTClaimsAsync(GetThirdPartyJWTClaimsRequest request);
    }
}
