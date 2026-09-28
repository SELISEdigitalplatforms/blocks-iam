namespace Iam.DomainService.SignupLinks;

public class QuerySignupLinksRequest
{
    public int Page { get; set; }
    public int PageSize { get; set; } = 20;
    public string? ConfigurationId { get; set; }
}
