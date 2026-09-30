namespace Iam.DomainService.SignupLinks;

public class GenerateSignupLinkRequest
{
    public string ConfigurationId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? OrganizationId { get; set; }
    public List<string>? Roles { get; set; }
    public List<string>? Permissions { get; set; }
    /// <summary>
    /// Oidc mode only, and only as a pair with <see cref="RedirectUri"/>: a redirect URI is
    /// meaningful only against a client, so the two never resolve independently.
    /// Both null or empty means "use the configuration's pair".
    /// </summary>
    public string? ClientId { get; set; }
    public string? RedirectUri { get; set; }
    public string? ForwardedTo { get; set; }
    public int? ExpiresInMinutes { get; set; }
}
