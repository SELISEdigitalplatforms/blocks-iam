using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Resources
{
    public class GetPermissionsRequest : BaseGetsRequest<GetPermissionFilter>
    {
        public List<string> Roles { get; set; } = new List<string>();
    }

    public class GetPermissionsResponse : BaseQueryListResponse<IQueryable<Permission>>
    {
    }
}
