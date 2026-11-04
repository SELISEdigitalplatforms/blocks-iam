using Blocks.Genesis;
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

            // An error page, or a web app's HTML served with 200 for an unknown path, is not
            // discovery metadata: answer 400 naming what came back instead of a JSON-parse 500
            // (Rollbar #1809/#1810). Only "not JSON at all" is caught here - a JSON document the
            // typed parse below cannot map still fails as before.
            if (!response.IsSuccessStatusCode || !IsJsonObject(json))
            {
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "no content type";
                throw new BlocksValidationException(
                    "OIDC discovery failed",
                    new Dictionary<string, string[]>
                    {
                        ["WellKnownUrl"] =
                        [
                            $"{wellKnownUrl} did not return OpenID Connect discovery metadata (HTTP {(int)response.StatusCode}, {contentType})."
                        ]
                    });
            }

            return JsonSerializer.Deserialize<OpenIdConnectConfiguration>(json);
        }

        private static bool IsJsonObject(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
