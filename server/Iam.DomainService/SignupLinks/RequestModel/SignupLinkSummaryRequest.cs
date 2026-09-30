namespace Iam.DomainService.SignupLinks;

public class SignupLinkSummaryRequest
{
    public string ConfigurationId { get; set; } = string.Empty;
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}
