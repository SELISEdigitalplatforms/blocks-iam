using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Users.ResponseModel
{
    public class GetLoginPolicyResponse : BaseResponse
    {
        public TenantLoginPolicy LoginPolicy { get; set; } = new();
    }
}
