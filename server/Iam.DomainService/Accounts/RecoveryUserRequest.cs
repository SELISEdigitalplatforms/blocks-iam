namespace Iam.DomainService.Accounts
{
    public class RecoveryUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? CaptchaCode { get; set; }
        public string? MailPurpose { get; set; }
    }


}
