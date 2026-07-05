using System.Text.Json.Serialization;

namespace Authentication.DomainService.Shared.Dtos
{
    /// <summary>
    /// Cache payload for <c>oidc_social_state:{state}</c> — links a social-provider callback
    /// back to the original OIDC flow via the OIDC <c>state</c> that initiated it.
    /// </summary>
    public sealed class OidcSocialStateContext
    {
        [JsonPropertyName("oidcState")]
        public string OidcState { get; set; } = string.Empty;

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }
}
