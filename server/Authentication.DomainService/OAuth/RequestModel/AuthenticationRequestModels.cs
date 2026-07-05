namespace Authentication.DomainService.OAuth.RequestModel
{
    using Iam.DomainService.Entities;
    using System.Text.Json.Serialization;

    public sealed class EmbeddedLoginRequest
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
        [JsonPropertyName("captcha_code")]
        public string? CaptchaCode { get; set; }

        [JsonPropertyName("mfa_id")]
        public string? MfaId { get; set; }

        [JsonPropertyName("mfa_code")]
        public string? MfaCode { get; set; }

        [JsonPropertyName("mfa_type")]
        public UserMfaType? MfaType { get; set; }
    }

    public sealed class SocialLoginRequest
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;
        [JsonPropertyName("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("mfa_id")]
        public string? MfaId { get; set; }

        [JsonPropertyName("mfa_code")]
        public string? MfaCode { get; set; }

        [JsonPropertyName("mfa_type")]
        public UserMfaType? MfaType { get; set; }
    }

    public sealed class OidcCallbackRequest
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;
    }

    public sealed class OidcCodeExchangeRequest
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

    public sealed class SwitchOrganizationRequest
    {
        [JsonPropertyName("organization_id")]
        public string OrganizationId { get; set; } = string.Empty;
    }

    public sealed class RefreshRequest
    {
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }
}