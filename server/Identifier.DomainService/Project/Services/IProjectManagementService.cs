using Identifier.DomainService.Dtos;
using Identifier.DomainService.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Identifier.DomainService.Projects
{
    public interface IProjectManagementService
    {
        Task<List<GroupedProjectsDto>> GetAllAsync(GetProjectsRequest request);
        Task<GetProjectResponse> GetAsync(string projectId);
        Task<IActionResult> GetProjectTokenValidationParametersAsync(string projectId);
        Task<ThirdPartyJWTClaims?> GetThirdPartyJWTClaimsAsync(GetThirdPartyJWTClaimsRequest request);
    }
}
