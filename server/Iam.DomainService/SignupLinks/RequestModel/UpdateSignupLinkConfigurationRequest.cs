namespace Iam.DomainService.SignupLinks;

public class UpdateSignupLinkConfigurationRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string>? DefaultRoles { get; set; }
    public List<string>? DefaultPermissions { get; set; }
    public string? ClientId { get; set; }
    public string? RedirectUri { get; set; }
    public string? DefaultForwardedTo { get; set; }
    public SignupLinkCredentialMode? CredentialMode { get; set; }
    public SignupLinkMode? Mode { get; set; }
    public string? JoinUrl { get; set; }
    public bool? SignInAfterActivation { get; set; }
    public int? DefaultLifetimeMinutes { get; set; }
    public int? DefaultMaxRedemptions { get; set; }
}
