using Iam.DomainService.Dtos;

namespace Authentication.DomainService.Security.Models
{
    public sealed class AuthHistoryDto
    {
        public string? Event { get; set; }
        public string? ActionBy { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceType { get; set; }
        public DeviceInformation? DeviceInformation { get; set; }
        public string? IpAddresses { get; set; }
        public string? SessionId { get; set; }
        public string? TenantId { get; set; }
        public string? ClientId { get; set; }
        public string? CorrelationId { get; set; }
        public string? Outcome { get; set; }
        public string? ReasonCode { get; set; }
        public string? RiskLevel { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}