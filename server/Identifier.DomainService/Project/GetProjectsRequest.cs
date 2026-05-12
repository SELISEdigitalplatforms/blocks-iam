using Blocks.Genesis;

namespace Identifier.DomainService.Projects
{
    public class GetProjectsRequest : BaseGetsRequest<GetProjectsFilter>
    {
        public string? TenantGroupId { get; set; }
    }
}
