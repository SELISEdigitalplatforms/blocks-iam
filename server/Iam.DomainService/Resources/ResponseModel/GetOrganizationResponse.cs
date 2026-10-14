using Blocks.Genesis;
using Iam.DomainService.Shared.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Resources.ResponseModel
{
    public class GetOrganizationResponse: BaseResponse
    {
        public Organization Organization { get; set; }
    }
}
