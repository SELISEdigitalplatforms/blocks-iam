using Blocks.Genesis;
using Identifier.DomainService.Dtos;
using Identifier.DomainService.Projects;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// Project Management Controller
    /// Manages project creation, configuration, and discovery
    /// Provides project metadata and organizational structure
    /// </summary>
    [ApiController]
    [Route("project")]
    public class ProjectController : ControllerBase
    {
        private readonly IProjectManagementService _projectManagementService;

        public ProjectController(IProjectManagementService projectManagementService)
        {
            _projectManagementService = projectManagementService;
        }

        /// <summary>
        /// Retrieve all projects grouped by hierarchy
        /// Returns projects with nested organization structure
        /// User must have project access permissions
        /// </summary>
        /// <param name="request">Query filters and pagination options</param>
        /// <returns>Grouped projects by organization</returns>
        /// <response code="200">Successfully retrieved projects</response>
        /// <response code="400">Invalid query parameters</response>
        /// <response code="401">Authentication required</response>
        [HttpGet("list")]
        //[ProtectedEndPoint("blocks-idp::project::getall")]
        public async Task<List<GroupedProjectsDto>> GetAll([FromQuery] GetProjectsRequest request)
        {
            return await _projectManagementService.GetAllAsync(request);
        }

        /// <summary>
        /// Retrieve detailed project information
        /// Returns complete project configuration and metadata
        /// User must have project access permissions
        /// </summary>
        /// <param name="projectId">Unique identifier of project to retrieve</param>
        /// <returns>Detailed project information with configuration</returns>
        /// <response code="200">Successfully retrieved project details</response>
        /// <response code="400">projectId parameter is required</response>
        /// <response code="401">Authentication required</response>
        /// <response code="404">Project not found</response>
        //[ProtectedEndPoint("blocks-idp::project::get")]
        [HttpGet("details")]
        public async Task<GetProjectResponse> Get([FromQuery] string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return new GetProjectResponse { Errors = new Dictionary<string, string> { { "empty_project_id", "projectId_should_not_be_empty" } } };

            return await _projectManagementService.GetAsync(projectId);
        }
    }
}
