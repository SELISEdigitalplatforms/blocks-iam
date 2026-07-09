using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Text.Json;

namespace Authentication.DomainService.Shared.Services
{
    /// <summary>
    /// Fetches OIDC discovery metadata (well-known configuration) from an external IdP.
    /// Uses <see cref="IHttpClientFactory"/> to avoid socket exhaustion from
    /// <c>new HttpClient()</c> and to respect HttpClientFactory lifecycle.
    /// </summary>
    public sealed class OidcDiscoveryClient
    {
        public const string HttpClientName = "oidc-discovery";

        private readonly IHttpClientFactory _httpClientFactory;

        public OidcDiscoveryClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<OpenIdConnectConfiguration?> GetMetadataAsync(string wellKnownUrl)
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var response = await httpClient.GetAsync(wellKnownUrl);
            string json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<OpenIdConnectConfiguration>(json);
        }
    }
}
