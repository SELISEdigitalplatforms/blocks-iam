namespace Iam.DomainService.SignupLinks;

public class SignupLinkSummaryResponse
{
    public string ConfigurationId { get; set; } = string.Empty;
    public string? ConfigurationName { get; set; }
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public long TotalGenerated { get; set; }
    public long Used { get; set; }
    public long NeverUsed { get; set; }
    public NeverUsedBreakdown NeverUsedBreakdown { get; set; } = new();
    public long RejectedAttempts { get; set; }
}

public class NeverUsedBreakdown
{
    public long Active { get; set; }
    public long Expired { get; set; }
    public long Revoked { get; set; }
}
