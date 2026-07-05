namespace Iam.DomainService.Dtos
{
    public class AccountActivityEvent
    {
        public string Code { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public bool PreventPostEvent { get; set; }
        public string? MailPurpose { get; set; }
    }
}
