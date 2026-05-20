using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Accounts
{
    public class ResetPasswordRequest : BaseAccountRequest
    {
        public bool LogoutFromAllDevices { get; set; }
    }

    
}
