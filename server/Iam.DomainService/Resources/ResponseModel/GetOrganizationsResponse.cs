using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Resources.ResponseModel
{
    public class GetOrganizationsResponse : BaseResponse
    {
        public List<Organization> Organizations { get; set; }
        public long TotalCount { get; set; }
    }
}
