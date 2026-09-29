namespace Iam.DomainService.SignupLinks;

/// <summary>
/// Redeem response. Exactly one of authorizeUrl / activationKey / loginUrl / mfaId is populated
/// depending on branch and credential mode (Phase 4).
/// </summary>
public class RedeemSignupLinkResponse
{
    /// <summary>"Oidc" or "Embedded". Discriminates the rest of this body.</summary>
    public string? Mode { get; set; }
    public string? AuthorizeUrl { get; set; }
    public string? ActivationKey { get; set; }
    public DateTime? ActivationKeyExpiresAtUtc { get; set; }
    public string? CredentialMode { get; set; }
    /// <summary>
    /// No longer populated. An existing account now completes with a session like every other
    /// redemption, rather than being handed a login page -- which for an embedded link was
    /// built from a client and redirect it does not have. Kept so an existing caller reading
    /// the field still compiles; it will always be null.
    /// </summary>
    public string? LoginUrl { get; set; }
    public string? MfaId { get; set; }
    public string? UserMfa { get; set; }
    public string? Error { get; set; }
}

public class RedeemSignupLinkErrorResponse
{
    public string Error { get; set; } = "invalid_link";
}
