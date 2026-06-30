using System.Text.Json.Serialization;

namespace Authentication.DomainService.Shared.Dtos
{
    /// <summary>
    /// Cache payload for <c>oidc_context:{oidcState}</c> — captures the original OIDC
    /// request parameters so a downstream social-provider callback can resume the flow.
    /// </summary>
    public sealed class OidcContext
    {
        [JsonPropertyName("clientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("providerClientId")]
        public string ProviderClientId { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("redirectUri")]
        public string RedirectUri { get; set; } = string.Empty;

        [JsonPropertyName("providerRedirectUri")]
        public string ProviderRedirectUri { get; set; } = string.Empty;

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("nonce")]
        public string? Nonce { get; set; }

        [JsonPropertyName("codeChallenge")]
        public string? CodeChallenge { get; set; }

        [JsonPropertyName("codeChallengeMethod")]
        public string? CodeChallengeMethod { get; set; }

        [JsonPropertyName("tenantId")]
        public string? TenantId { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }
}
