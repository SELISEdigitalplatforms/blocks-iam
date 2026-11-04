using Authentication.DomainService.Utilities;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Services
{
    /// <summary>
    /// Supplies <see cref="IDefaultOidcClientResolver"/> to Iam.DomainService, which owns
    /// the account-action email builders but cannot see OidcClientRegistration directly.
    /// </summary>
    public sealed class DefaultOidcClientResolver : IDefaultOidcClientResolver
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ILogger<DefaultOidcClientResolver> _logger;

        public DefaultOidcClientResolver(
            IAuthenticationRepository authenticationRepository,
            ILogger<DefaultOidcClientResolver> logger)
        {
            _authenticationRepository = authenticationRepository;
            _logger = logger;
        }

        public async Task<DefaultOidcClient?> GetDefaultClientAsync()
        {
            try
            {
                var clients = await _authenticationRepository.GetOIDCCredentialsByTenantAsync();

                // "First active with a redirect URI" is the agreed rule for portal invites.
                // A client without a registered redirect URI is unusable here: the value is
                // validated against RedirectUris at the authorize endpoint. Only web URIs count:
                // an emailed link opens in a browser, where a mobile app scheme leads nowhere.
                var client = clients?.FirstOrDefault(c =>
                    c.IsActive
                    && !string.IsNullOrWhiteSpace(c.ClientId)
                    && c.RedirectUris.Any(RedirectUriSchemes.IsWebUri));

                if (client == null)
                {
                    _logger.LogInformation("No active OIDC client with a web redirect URI found for the current tenant");
                    return null;
                }

                var redirectUri = client.RedirectUris.First(RedirectUriSchemes.IsWebUri);
                return new DefaultOidcClient(client.ClientId, redirectUri);
            }
            catch (Exception ex)
            {
                // The caller falls back to a context-free link; a lookup failure must not
                // stop an activation or recovery email from going out.
                _logger.LogError(ex, "Failed to resolve the default OIDC client for the current tenant");
                return null;
            }
        }
    }
}
