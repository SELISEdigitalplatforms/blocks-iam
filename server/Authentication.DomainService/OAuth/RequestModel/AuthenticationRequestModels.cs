namespace Authentication.DomainService.OAuth.RequestModel
{
    using System.Text.Json.Serialization;

    public class EmbeddedLoginRequest
    {
        public string? ClientId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class SocialLoginRequest
    {
        public string? ClientId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? Provider { get; set; }
    }

    public class OidcCallbackRequest
    {
        public string Code { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    public class OidcCodeExchangeRequest
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("code_verifier")]
        public string CodeVerifier { get; set; } = string.Empty;

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("redirect_uri")]
        public string RedirectUri { get; set; } = string.Empty;

        [JsonPropertyName("tenant_id")]
        public string? TenantId { get; set; }
    }

    public class SwitchOrganizationRequest
    {
        public string OrganizationId { get; set; } = string.Empty;
    }

    public class RefreshRequest
    {
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }
    }
}