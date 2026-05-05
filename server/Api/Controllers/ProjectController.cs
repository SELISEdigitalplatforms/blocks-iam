using Blocks.Genesis;
using Identifier.DomainService.Dtos;
using Identifier.DomainService.Projects;
using Microsoft.AspNetCore.Mvc;


namespace Api.Controllers
{
    [ApiController]
    [Route("project")]

    public class ProjectController : ControllerBase
    {
        private readonly IProjectManagementService _projectManagementService;

        public ProjectController(IProjectManagementService projectManagementService)
        {
            _projectManagementService = projectManagementService;
        }


        [HttpGet("list")]
        [ProtectedEndPoint]
        public async Task<List<GroupedProjectsDto>> GetAll([FromQuery] GetProjectsRequest request)
        {
            return await _projectManagementService.GetAllAsync(request);
        }

        [ProtectedEndPoint]
        [HttpGet("details")]
        public async Task<GetProjectResponse> Get([FromQuery] string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return new GetProjectResponse { Errors = new Dictionary<string, string> { { "empty_project_id", "projectId_should_not_be_empty" } } };

            return await _projectManagementService.GetAsync(projectId);
        }
    }
}
