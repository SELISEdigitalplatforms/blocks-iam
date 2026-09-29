namespace Iam.DomainService.SignupLinks;

public class SignupLinkContextResponse
{
    public bool Valid { get; set; }
    public string? CredentialMode { get; set; }
    public string? FirstName { get; set; }
    public string? MaskedEmail { get; set; }
    public string? ApplicationName { get; set; }

    /// <summary>"Oidc" or "Embedded" -- lets a join screen refuse a link of the other mode.</summary>
    public string? Mode { get; set; }

    public static SignupLinkContextResponse Invalid() => new() { Valid = false };
}
