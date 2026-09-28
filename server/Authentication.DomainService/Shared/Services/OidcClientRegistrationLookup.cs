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

        return new OidcClientRegistrationInfo(
            client.ClientId ?? client.ItemId,
            client.RedirectUris ?? [],
            client.IsActive,
            client.ClientName);
    }
}
