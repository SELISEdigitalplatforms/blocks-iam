namespace Iam.DomainService.SignupLinks;

public class GenerateSignupLinkResponse
{
    public string LinkId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public bool EmailAlreadyExists { get; set; }
}
