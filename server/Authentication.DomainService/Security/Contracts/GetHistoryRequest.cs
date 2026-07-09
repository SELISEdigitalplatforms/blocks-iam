using Blocks.Genesis;

namespace Authentication.DomainService.Security.Contracts
{
    public sealed class AuthHistoryFilter
    {
        public string? EventType { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? IpAddress { get; set; }
        public string? Device { get; set; }
    }

    public sealed class GetHistoryRequest : BaseGetsRequest<AuthHistoryFilter>
    {
    }
}