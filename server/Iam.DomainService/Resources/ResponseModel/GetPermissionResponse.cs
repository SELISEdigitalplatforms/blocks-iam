using Blocks.Genesis;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Resources
{
    public class GetPermissionResponse : BaseQueryResponse<Permission>
    {
    }

    public class GetPermissionRequest
    {
        public string Id { get; set; }
    }
}
