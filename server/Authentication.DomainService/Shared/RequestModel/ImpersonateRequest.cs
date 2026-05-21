using System.Text.Json.Serialization;

namespace Authentication.DomainService.Shared.RequestModel
{
    public class ImpersonateRequest
    {
        [JsonPropertyName("targeted_tenant_id")]
        public string TargetTenantId { get; set; }

        [JsonPropertyName("organization_id")]
        public string? OrganizationId { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("impersonation_id")]
        public string? ImpersonationId { get; set; }

    }

    public class StopImpersonationRequest
    {
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("impersonation_id")]
        public string? ImpersonationId { get; set; }
    }

    public class ImpersonateResponse
    {
        public bool impersonation_mode { get; set; } = true;
        public bool org_switched { get; set; } = false;
    }

    public class StopImpersonationResponse
    {
        public bool impersonation_mode { get; set; } = false;
        public string? error { get; set; }
    }
}
