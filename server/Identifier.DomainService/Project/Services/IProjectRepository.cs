using Blocks.Genesis;
using Identifier.DomainService.Entities;
using Identifier.DomainService.Dtos;
using Identifier.DomainService.Shared;

namespace Identifier.DomainService.Projects
{
    public interface IProjectRepository
    {
        Task<Tenant> GetByIdAsync(string itemId);
        Task<List<GroupedProjectsDto>> GetAllByLastModifiedDateAsync(GetProjectsRequest request);
        Task<List<ProjectStatusTracer>> GetAllUnfinishedProjectAsync();
        Task<long> GetProjectCountAsync();
        Task<Tenant> GetByTenantIdAsync(string tenantId);
        Task<List<SsoInfo>> GetSsoInfoAsync();
        Task<BlocksGuid> GetBlocksGuidAsync(string tenantGroupId);
        Task<ThirdPartyJWTClaims> GetThirdPartyJWTClaimsAsync(string itemId);
        Task<bool> IsExistingEnviroment(List<string> enviroments, string tenantGroupId);
        Task<List<Project>> GetSharedProjectsAsync(string? tenantGroupId);
        Task<List<Project>> GetProjectPeoplesAsync(string tenantGroupId);
        Task<List<string>> GetProjectIdsByGroupId(string projectGroupId);
    }
}
