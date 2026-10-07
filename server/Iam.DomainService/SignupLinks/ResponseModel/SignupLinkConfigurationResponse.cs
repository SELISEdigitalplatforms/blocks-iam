namespace Iam.DomainService.SignupLinks;

public class SignupLinkConfigurationResponse
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> DefaultRoles { get; set; } = new();
    public List<string> DefaultPermissions { get; set; } = new();
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? DefaultForwardedTo { get; set; }
    public SignupLinkCredentialMode CredentialMode { get; set; }
    public SignupLinkMode Mode { get; set; }
    public string? JoinUrl { get; set; }
    public bool SignInAfterActivation { get; set; }
    public bool RequireExistingUserPassword { get; set; }
    public int DefaultLifetimeMinutes { get; set; }
    public int? DefaultMaxRedemptions { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime LastUpdatedDate { get; set; }
}
