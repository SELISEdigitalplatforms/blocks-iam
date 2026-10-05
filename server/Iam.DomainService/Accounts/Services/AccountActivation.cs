using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// What follows an account becoming active. Shared by the activation email and by a signup
    /// link that activates a pending account, so the two paths cannot drift apart.
    /// </summary>
    public static class AccountActivation
    {
        /// <summary>
        /// Retires every activation key the user still holds. A key lives both in the cache the
        /// activation endpoint reads and in UserKeyMaps; both have to go, or an earlier invite
        /// email would keep working against an account that is already active.
        /// </summary>
        public static async Task RetireActivationKeysAsync(
            IIdentityAccessManagementRepository repository,
            ICacheClient cacheClient,
            string userId)
        {
            ArgumentNullException.ThrowIfNull(repository);
            ArgumentNullException.ThrowIfNull(cacheClient);

            var keys = (await repository.GetActiveUserKeyMapAsync(userId))?.Select(x => x.Key) ?? [];
            await Task.WhenAll(keys.Select(key => cacheClient.RemoveKeyAsync(key)));

            await repository.UpdateUserKeyMapActivationAsync(userId);
        }

        /// <summary>
        /// The activity event recorded when an account is activated.
        /// </summary>
        public static UserActivityEvent ActivatedEvent(string userId, string source) => new()
        {
            UserId = userId,
            Category = UserActivityCategory.Account,
            Event = AccountEvents.ActivateAccount,
            Source = source
        };
    }
}
