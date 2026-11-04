using Blocks.Genesis;
using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Resources
{
    public class GetRoleResponse : BaseQueryResponse<Role>
    {
    }

    public class GetRoleRequest
    {
        public string Id { get; set; }
    }
}
