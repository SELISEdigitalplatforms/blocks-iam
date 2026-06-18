using Identifier.DomainService.Dtos;
using Identifier.DomainService.Entities;
using Identifier.DomainService.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Identifier.DomainService.Projects
{
    public class ProjectManagementService : IProjectManagementService
    {
        private readonly IProjectRepository _projectRepository;

        public ProjectManagementService(IProjectRepository projectRepository)
        {
            _projectRepository = projectRepository;
        }

        public async Task<List<GroupedProjectsDto>> GetAllAsync(GetProjectsRequest request)
        {
            return await _projectRepository.GetAllByLastModifiedDateAsync(request);
        }

        public async Task<GetProjectResponse> GetAsync(string projectId)
        {
            return await MapIntoProjectAsync(projectId);
        }

        private async Task<GetProjectResponse> MapIntoProjectAsync(string projectId)
        {
            var repoProject = await _projectRepository.GetByIdAsync(projectId);

            if (repoProject == null)
            {
                return new GetProjectResponse { Errors = new Dictionary<string, string> { { "project_not_exist", $"project_with_id_{projectId}_not_exist_into_our_system" } } };
            }

            string tenantSlug = string.Empty;
            var blocksGuid = await _projectRepository.GetBlocksGuidAsync(repoProject.TenantGroupId);
            if (blocksGuid is not null)
            {
                tenantSlug = $"{IdentifierHelper.EnvironmentMapper(repoProject.Environment)}{blocksGuid.EncodedValue}";
            }

            var project = new GetProjectResponseData
            {
                Name = repoProject.Name,
                Applications = repoProject.Applications,
                ItemId = repoProject.ItemId,
                CreatedDate = repoProject.CreatedDate,
                LastUpdatedDate = repoProject.LastUpdatedDate,
                LastUpdatedBy = repoProject.LastUpdatedBy,
               // OrganizationIds = repoProject.OrganizationIds,
                CreatedBy = repoProject.CreatedBy,
                Tags = repoProject.Tags,
                TenantId = repoProject.TenantId,
                IsDisabled = repoProject.IsDisabled,
                Environment = repoProject.Environment,
                TenantGroupId = repoProject.TenantGroupId,
                TenantSlug = tenantSlug
            };

            return new GetProjectResponse { Data = project };
        }

        public async Task<IActionResult> GetProjectTokenValidationParametersAsync(string projectId)
        {
            var project = await _projectRepository.GetByTenantIdAsync(projectId);

            if (project == null)
            {
                return new NotFoundObjectResult(new { error = "Project not found" });
            }

            var tokenParams = new
            {
                IsConfigured = project?.ThirdPartyJwtTokenParameters is not null,
                ProviderName = project?.ThirdPartyJwtTokenParameters?.ProviderName,
                Issuer = project?.ThirdPartyJwtTokenParameters?.Issuer,
                Audiences = project?.ThirdPartyJwtTokenParameters?.Audiences,
                PublicCertificatePath = project?.ThirdPartyJwtTokenParameters?.PublicCertificatePath,
                JwksUrl = project?.ThirdPartyJwtTokenParameters?.JwksUrl,
                CookieKey = project?.ThirdPartyJwtTokenParameters?.CookieKey
            };
            return new OkObjectResult(tokenParams);
        }

        public async Task<ThirdPartyJWTClaims?> GetThirdPartyJWTClaimsAsync(GetThirdPartyJWTClaimsRequest request)
        {
            return await _projectRepository.GetThirdPartyJWTClaimsAsync(request.ItemId);
        }

    }

}
