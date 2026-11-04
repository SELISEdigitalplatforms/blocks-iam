using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Services;

namespace Authentication.DomainService.Services;

/// <summary>
/// Auth-side implementation of the Iam abstraction for OIDC client + redirect validation.
/// </summary>
public sealed class OidcClientRegistrationLookup : IOidcClientRegistrationLookup
{
    private readonly IAuthenticationRepository _repository;

    public OidcClientRegistrationLookup(IAuthenticationRepository repository)
    {
        _repository = repository;
    }

    public async Task<OidcClientRegistrationInfo?> GetByClientIdAsync(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var client = await _repository.GetOidcClientRegistrationAsync(clientId);
        if (client == null)
        {
            return null;
        }

        // Looked up by the id the caller asked about, as redemption does, so both answer alike.
        var provider = await _repository.GetIdentityProviderByClientIdAsync(clientId);

        return new OidcClientRegistrationInfo(
            client.ClientId ?? client.ItemId,
            client.RedirectUris ?? [],
            client.IsActive,
            client.ClientName,
            ToProviderState(provider));
    }

    private static OidcIdentityProviderState ToProviderState(IdentityProvider? provider)
    {
        if (provider == null)
        {
            return OidcIdentityProviderState.None;
        }

        return provider.IsActive ? OidcIdentityProviderState.Active : OidcIdentityProviderState.Inactive;
    }
}
