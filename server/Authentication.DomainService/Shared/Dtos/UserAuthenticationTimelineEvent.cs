using Iam.DomainService.Dtos;

namespace Authentication.DomainService.Dtos
{
    public class UserAuthenticationTimelineEvent
    {
        public DeviceInformation? DeviceInformation { get; set; }
        public string? IpAddresses { get; set; }
        public string? Event { get; set; }
        public string? ActionBy { get; set; }
        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public string? ClientId { get; set; }
        public string? SessionId { get; set; }
        public string? CorrelationId { get; set; }
        public string? Outcome { get; set; }
        public string? ReasonCode { get; set; }
        public string? RiskLevel { get; set; }
    }
}
