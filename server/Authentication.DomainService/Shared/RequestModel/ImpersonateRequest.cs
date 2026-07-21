using System.Text.Json.Serialization;

namespace Authentication.DomainService.Shared.RequestModel
{
    public sealed class ImpersonateRequest
    {
        [JsonPropertyName("targeted_tenant_id")]
        public string TargetTenantId { get; set; }

        [JsonPropertyName("organization_id")]
        public string? OrganizationId { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("impersonation_id")]
        public string? ImpersonationId { get; set; }

        public string? ImpersontingUserId { get; set; }

        [JsonPropertyName("client_id")]
        public string? ClientId {get; set;}

    }

    public sealed class StopImpersonationRequest
    {
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("impersonation_id")]
        public string? ImpersonationId { get; set; }
    }

    public sealed class ImpersonateResponse
    {
        public bool impersonation_mode { get; set; } = true;
        public bool org_switched { get; set; } = false;
    }

    public sealed class StopImpersonationResponse
    {
        public bool impersonation_mode { get; set; } = false;
        public string? error { get; set; }
    }
}
