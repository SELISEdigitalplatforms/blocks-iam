using Blocks.Genesis;

namespace Iam.DomainService.Accounts
{
    public class ChangePasswordRequest
    {
        public string NewPassword { get; set; } = string.Empty;
        public string OldPassword { get; set; } = string.Empty;
    }


}
