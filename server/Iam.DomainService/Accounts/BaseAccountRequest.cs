namespace Iam.DomainService.Accounts
{
    public class BaseAccountRequest
    {
        public string Code { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string? CaptchaCode { get; set; }
    }
}
