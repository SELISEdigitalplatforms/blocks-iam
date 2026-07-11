namespace Authentication.DomainService.Security.Models
{
    public sealed class SessionDetailsDto
    {
        public SessionOverviewDto? Overview { get; set; }
        public List<ApplicationDto> Applications { get; set; } = new();
        public List<TimelineEventDto> Timeline { get; set; } = new();
    }
}
