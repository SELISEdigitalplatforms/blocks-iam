namespace Authentication.DomainService.Security.Models
{
    public sealed class ActivityPageResponse
    {
        public IReadOnlyList<ActivityItemDto> Items { get; set; } = new List<ActivityItemDto>();
        public long TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
