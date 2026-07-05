using Authentication.DomainService.Services;
using Blocks.Genesis;

namespace Authentication.DomainService.OAuth
{
    /// <summary>
    /// Exchanges an authorization code for tokens at an external IdP's token endpoint.
    /// Extracted from <c>IdpService</c> to reduce DI count (S107).
    /// </summary>
    public sealed class IdpTokenExchangeClient
    {
        private readonly IHttpService _httpService;

        public IdpTokenExchangeClient(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task<(OidcTokenEndpointResponse? Response, string Error)> ExchangeCodeForTokenAsync(
            string tokenEndpoint,
            Dictionary<string, string> form,
            CancellationToken cancellationToken,
            int? timeoutSeconds = null)
        {
            var (response, error) = await _httpService.SendFormUrlEncoded<OidcTokenEndpointResponse>(
                HttpMethod.Post,
                form,
                tokenEndpoint,
                cancellationToken: cancellationToken,
                timeoutSeconds: timeoutSeconds);

            if (!string.IsNullOrWhiteSpace(error))
            {
                return (null, error);
            }

            return (response, error);
        }
    }
}
