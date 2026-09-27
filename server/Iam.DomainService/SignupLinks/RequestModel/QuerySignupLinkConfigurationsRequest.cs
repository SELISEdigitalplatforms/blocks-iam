namespace Iam.DomainService.SignupLinks;

public class QuerySignupLinkConfigurationsRequest
{
    public int Page { get; set; }
    public int PageSize { get; set; } = 20;
    public bool IncludeInactive { get; set; }
    public string? Search { get; set; }
}
