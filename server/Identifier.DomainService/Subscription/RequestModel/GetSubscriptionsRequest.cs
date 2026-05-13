using Blocks.Genesis;

namespace Identifier.DomainService.Subscription.RequestModel
{
    public class GetSubscriptionsRequest : IProjectKey
    {
        public string? ProjectKey { get ; set ; }
    }
}
