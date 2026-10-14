using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Resources
{
    public class GetRolesRequest : BaseGetsRequest<GetRolesFilter>
    {
        public string? OrganizationId {get; set;}
    }
    public class GetRolesResponse : BaseQueryListResponse<IQueryable<Role>>
    {
    }
}
