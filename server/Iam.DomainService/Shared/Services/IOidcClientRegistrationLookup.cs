namespace Iam.DomainService.Services;

/// <summary>
/// Tenant-scoped OIDC client lookup for IAM features that must validate clientId + redirect URIs
/// without taking a project reference on Authentication.DomainService (Auth already references Iam).
/// Implemented in Authentication.DomainService over IAuthenticationRepository.GetOidcClientRegistrationAsync.
/// </summary>
public interface IOidcClientRegistrationLookup
{
    Task<OidcClientRegistrationInfo?> GetByClientIdAsync(string clientId);
}

public sealed record OidcClientRegistrationInfo(
    string ClientId,
    IReadOnlyList<string> RedirectUris,
    bool IsActive,
    string? ClientName = null,
    OidcIdentityProviderState IdentityProvider = OidcIdentityProviderState.None);

/// <summary>
/// The Blocks IdentityProvider mirroring a client, which saving the client with
/// RegisterAsIdentityProvider writes. An OIDC signup link finishes at <c>/idp/callback</c>,
/// which looks that provider up, so a client without an active one cannot complete a link.
/// </summary>
public enum OidcIdentityProviderState
{
    None = 0,
    Inactive = 1,
    Active = 2
}
