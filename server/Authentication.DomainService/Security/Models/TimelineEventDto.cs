namespace Authentication.DomainService.Security.Models
{
    public sealed class TimelineEventDto
    {
        public TimelineEventType Type { get; set; }
        public DateTime At { get; set; }
        public string? Event { get; set; }
        public string? Outcome { get; set; }
        public string? ReasonCode { get; set; }
        public string? IpAddress { get; set; }
        public string? DeviceName { get; set; }
        public string? ClientId { get; set; }
    }
}
