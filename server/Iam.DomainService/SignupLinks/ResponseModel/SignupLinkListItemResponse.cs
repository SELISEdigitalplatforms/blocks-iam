namespace Iam.DomainService.SignupLinks;

public class SignupLinkListItemResponse
{
    public string LinkId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public string ConfigurationId { get; set; } = string.Empty;
    public SignupLinkStatus Status { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
}

public class SignupLinkListResponse
{
    public List<SignupLinkListItemResponse> Items { get; set; } = new();
    public long TotalCount { get; set; }
}
