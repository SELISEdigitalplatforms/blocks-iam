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
    string? ClientName = null);
