namespace Iam.DomainService.Dtos
{
    public sealed class ActivityContext
    {
        public string? IpAddress { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceType { get; set; }
        public string? UserAgent { get; set; }
        public DeviceInformation? DeviceInformation { get; set; }
    }
}