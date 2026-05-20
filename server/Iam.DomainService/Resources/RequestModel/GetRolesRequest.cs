using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Resources
{
    public class GetRolesRequest : BaseGetsRequest<GetRolesFilter>
    {

    }
    public class GetRolesResponse : BaseQueryListResponse<IQueryable<Role>>
    {
    }
}
