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
        public string Provider { get; set; } = string.Empty;
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

    public class ImpersonationRequest
    {
        public string TargetTenantId { get; set; } = string.Empty;

        [JsonPropertyName("orgId")]
        public string? OrgId { get; set; }

        [JsonPropertyName("organizationId")]
        public string? OrganizationId { get; set; }
    }

    public class ImpersonationState
    {
        public string RootTenantId { get; set; } = string.Empty;
        public string TargetTenantId { get; set; } = string.Empty;
        public string OrgId { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
    }

    public class RefreshRequest
    {
        public string? RefreshToken { get; set; }
    }
}