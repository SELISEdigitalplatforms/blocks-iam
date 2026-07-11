using System.Text.Json.Serialization;

namespace Authentication.DomainService.Security.Models
{
    public sealed class SessionOverviewDto
    {
        public string SessionId { get; set; } = "";
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SessionStatus Status { get; set; }
        public bool IsCurrent { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceModel { get; set; }
        public string? OperatingSystem { get; set; }
        public string? Browser { get; set; }
        public string? IpAddress { get; set; }
        public string? Location { get; set; }
        public List<string> ClientIds { get; set; } = new();
        public List<string> OrganizationIds { get; set; } = new();
        public DateTime StartedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public DateTime AbsoluteExpiry { get; set; }
        public DateTime IdleExpiry { get; set; }
    }
}
