using Blocks.Genesis;

namespace Iam.DomainService.Accounts
{
    public class RecoveryUserRequest
    {
        public string Email { get; set; }
        public string? CaptchaCode { get; set; }
        public string? MailPurpose { get; set; }
    }


}
