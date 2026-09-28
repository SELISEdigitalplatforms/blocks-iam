namespace Iam.DomainService.SignupLinks;

public class SignupLinkContextResponse
{
    public bool Valid { get; set; }
    public string? CredentialMode { get; set; }
    public string? FirstName { get; set; }
    public string? MaskedEmail { get; set; }
    public string? ApplicationName { get; set; }

    public static SignupLinkContextResponse Invalid() => new() { Valid = false };
}
