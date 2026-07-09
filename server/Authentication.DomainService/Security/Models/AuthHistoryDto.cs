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
        public DateTime CreatedDate { get; set; }
    }
}