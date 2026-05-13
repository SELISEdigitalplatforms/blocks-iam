using Identifier.DomainService.Subscription.RequestModel;
using Identifier.DomainService.Subscription.ResponseModel;

namespace Identifier.DomainService.Subscription.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ISubscriptionRepository _resourceRepository;

        public SubscriptionService(ISubscriptionRepository resourceRepository)
        {
            _resourceRepository = resourceRepository;
        }

        public async Task<GetSubscriptionsResponse> GetSubscriptionsAsync(GetSubscriptionsRequest request)
        {
            var subscriptions = await _resourceRepository.GetSubscriptionsAsync();
            return new GetSubscriptionsResponse
            {
                Subscriptions = subscriptions,
                IsSuccess = true
            };
        }
    }
}
