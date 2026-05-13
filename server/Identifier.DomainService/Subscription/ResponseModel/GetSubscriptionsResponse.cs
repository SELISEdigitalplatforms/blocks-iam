using Blocks.Genesis;
using Identifier.DomainService.Entities;

namespace Identifier.DomainService.Subscription.ResponseModel
{
    public class GetSubscriptionsResponse : BaseResponse
    {
        public List<ResourceLimit> Subscriptions { get; set; } = [];
    }
}
