using Blocks.Genesis;

namespace Iam.DomainService.Accounts
{
    public class ResendActivationRequest
    {
        public string UserId { get; set; }
        public string? MailPurpose { get; set; }
    }


}
