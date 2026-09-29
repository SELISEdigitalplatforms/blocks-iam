namespace Iam.DomainService.SignupLinks;

public class CreateSignupLinkConfigurationRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> DefaultRoles { get; set; } = new();
    public List<string> DefaultPermissions { get; set; } = new();
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? DefaultForwardedTo { get; set; }
    public SignupLinkCredentialMode? CredentialMode { get; set; }

    /// <summary>Absent means Oidc, so callers predating embedded mode keep working.</summary>
    public SignupLinkMode? Mode { get; set; }
    public string? JoinUrl { get; set; }
    public int? DefaultLifetimeMinutes { get; set; }
    public int? DefaultMaxRedemptions { get; set; }
}
