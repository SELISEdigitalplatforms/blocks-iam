using Blocks.Genesis;

namespace Authentication.DomainService.Security.Contracts
{
    public sealed class SessionFilter
    {
        public string? ClientId { get; set; }
        public bool? ActiveOnly { get; set; }
    }

    public sealed class GetSessionsRequest : BaseGetsRequest<SessionFilter>
    {
    }
}