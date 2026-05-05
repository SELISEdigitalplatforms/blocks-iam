using Identifier.DomainService.Subscription.RequestModel;
using Identifier.DomainService.Subscription.ResponseModel;

namespace Identifier.DomainService.Subscription.Services
{
    public interface ISubscriptionService
    {
        public Task<GetSubscriptionsResponse> GetSubscriptionsAsync(GetSubscriptionsRequest request);
    }
}
