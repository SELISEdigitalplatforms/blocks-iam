using Blocks.Genesis;
using Identifier.DomainService.Entities;


namespace Identifier.DomainService.Projects
{
    public class GetProjectRequest
    {
        public string ProjectId { get; set; }
    }

    public class GetProjectResponse : BaseQueryResponse<GetProjectResponseData>
    {

    }
    public class GetProjectResponseData : Project
    {
        public string TenantSlug { get; set; }
    }
}
