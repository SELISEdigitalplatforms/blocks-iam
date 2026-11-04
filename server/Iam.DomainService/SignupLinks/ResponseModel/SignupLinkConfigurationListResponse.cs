namespace Iam.DomainService.SignupLinks;

public class SignupLinkConfigurationListResponse
{
    public List<SignupLinkConfigurationResponse> Items { get; set; } = new();
    public long TotalCount { get; set; }
}
