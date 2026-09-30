namespace Iam.DomainService.SignupLinks;

public class GenerateSignupLinkResponse
{
    public string LinkId { get; set; } = string.Empty;
    /// <summary>
    /// Oidc: the IAM-hosted join page. Embedded: composed from the configuration's JoinUrl,
    /// or null when it has none and the caller builds its own link from <see cref="Code"/>.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// The raw one-time code. It already travelled inside <see cref="Url"/>, so naming it
    /// separately discloses nothing new; it still appears in this response and nowhere else.
    /// </summary>
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public bool EmailAlreadyExists { get; set; }
}
