using Identifier.DomainService.Entities;

namespace Identifier.DomainService.Subscription.Services
{
    public interface ISubscriptionRepository
    {
        public Task<List<ResourceLimit>> GetSubscriptionsAsync();
    }
}
