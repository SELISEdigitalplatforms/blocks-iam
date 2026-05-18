using Blocks.Genesis;
using Iam.DomainService.Entities;

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
