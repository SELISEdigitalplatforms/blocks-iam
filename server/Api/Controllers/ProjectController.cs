using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Projects;
using Microsoft.AspNetCore.Mvc;


namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class ProjectController : ControllerBase
    {
        private readonly IProjectManagementService _projectManagementService;

        public ProjectController(IProjectManagementService projectManagementService)
        {
            _projectManagementService = projectManagementService;
        }


        [HttpGet]
        [ProtectedEndPoint]
        public async Task<List<GroupedProjectsDto>> Gets([FromQuery] GetProjectsRequest request)
        {
            return await _projectManagementService.GetAllAsync(request);
        }

        [ProtectedEndPoint]
        [HttpGet]
        public async Task<GetProjectResponse> Get([FromQuery] string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return new GetProjectResponse { Errors = new Dictionary<string, string> { { "empty_project_id", "projectId_should_not_be_empty" } } };

            return await _projectManagementService.GetAsync(projectId);
        }
    }
}
