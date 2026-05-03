namespace DomainService.OAuth.RequestModel
{
    using System.Text.Json.Serialization;

    public class EmbeddedLoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("org_id")]
        public string? OrgId { get; set; }

        [JsonPropertyName("organization_id")]
        public string? OrganizationId
        {
            get => OrgId;
            set => OrgId = value;
        }
    }

    public class SocialLoginRequest
    {
        public string Code { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("org_id")]
        public string? OrgId { get; set; }

        [JsonPropertyName("organization_id")]
        public string? OrganizationId
        {
            get => OrgId;
            set => OrgId = value;
        }
    }

    public class SwitchOrganizationRequest
    {
        public string OrganizationId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
    }

    public class ImpersonationRequest
    {
        public string TargetTenantId { get; set; } = string.Empty;

        [JsonPropertyName("orgId")]
        public string? OrgId { get; set; }

        [JsonPropertyName("organizationId")]
        public string? OrganizationId { get; set; }

        public string? ClientId { get; set; }
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
        public string? ClientId { get; set; }
        public string? OrganizationId { get; set; }
    }
}