namespace Iam.DomainService.Accounts
{
    public class ResendActivationRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string? MailPurpose { get; set; }
    }


}
